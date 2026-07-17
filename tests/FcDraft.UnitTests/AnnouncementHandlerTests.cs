using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Features.Announcements;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Announcements;
using FcDraft.Infrastructure.Drafts;
using FcDraft.Infrastructure.Email;
using FcDraft.Infrastructure.Notifications;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the PR-21 §9.8 announcement slice: audience resolution (active accounts only), the preview's
/// opt-out split, the confirmed-count conflict gate, the §9.9 opt-out (email suppressed, in-app notice
/// always lands), the batch throttle, and the audited attribution — all against the same in-memory
/// implementations the API's in-memory foundation runs on.
/// </summary>
public sealed class AnnouncementHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 07, 16, 12, 00, 00, TimeSpan.Zero);

    private readonly FakeIdentityDirectory _identity = new();
    private readonly InMemoryDraftStore _drafts = new();
    private readonly InMemoryAnnouncementStore _announcements = new();
    private readonly InMemoryUserNotificationStore _notifications = new();
    private readonly RecordingEmailQueue _emails = new();
    private readonly InMemoryEmailOutbox _outbox = new(TimeProvider.System);
    private readonly RecordingSecurityAuditService _audit = new();
    private readonly InMemoryTransactionRunner _runner = new();
    private readonly TestClock _clock = new(T0);

    private PreviewAnnouncementQueryHandler Preview() => new(_identity, _drafts);

    private SendAnnouncementCommandHandler Send() => new(
        _identity, _drafts, _announcements, _notifications, _emails, _outbox, _audit, _runner, _clock);

    [Fact]
    public async Task Preview_resolves_active_players_and_reports_the_opt_out_split()
    {
        var active1 = _identity.Add("Active One");
        var active2 = _identity.Add("Active Two");
        var optedOut = _identity.Add("Opted Out");
        optedOut.OptionalEmailOptOut = true;
        _identity.Add("Gone", AccountStatus.Deactivated);

        var preview = await Preview().Handle(
            new PreviewAnnouncementQuery("Subject", "Body", AnnouncementAudiences.All, null), default);

        Assert.Equal(3, preview.RecipientCount);          // deactivated accounts are never addressed
        Assert.Equal(2, preview.EmailRecipientCount);
        Assert.Equal(1, preview.OptedOutCount);
        Assert.Equal("All active players", preview.AudienceLabel);
        Assert.NotEqual(default, active1.Id);
        Assert.NotEqual(default, active2.Id);
    }

    [Fact]
    public async Task A_send_confirmed_against_a_stale_audience_count_conflicts_and_writes_nothing()
    {
        _identity.Add("One");
        _identity.Add("Two");
        var admin = _identity.Add("Admin", role: UserRole.Admin);

        // The admin previewed 2 recipients; a third player activated before the confirmation landed.
        _identity.Add("Latecomer");

        await Assert.ThrowsAsync<ConflictAppException>(() => Send().Handle(
            new SendAnnouncementCommand(
                "Subject", "Body", AnnouncementAudiences.All, null,
                ConfirmedRecipientCount: 3, admin.Id, admin.Email), default));

        Assert.Empty(await _announcements.ListRecentAsync(10, default));
        Assert.Empty(_emails.AnnouncementEmails);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task A_confirmed_send_notifies_everyone_but_emails_only_those_who_accept_optional_email()
    {
        var player = _identity.Add("Player");
        var optedOut = _identity.Add("Quiet");
        optedOut.OptionalEmailOptOut = true;
        var admin = _identity.Add("Admin", role: UserRole.Admin);

        var sent = await Send().Handle(
            new SendAnnouncementCommand(
                "Dataset refreshed", "The FC 26 pool was updated.", AnnouncementAudiences.All, null,
                ConfirmedRecipientCount: 3, admin.Id, admin.Email), default);

        // Campaign metadata (§9.8): audience definition, counts, requester, and time are recorded.
        var record = Assert.Single(await _announcements.ListRecentAsync(10, default));
        Assert.Equal(sent.Id, record.Id);
        Assert.Equal(3, record.RecipientCount);
        Assert.Equal(2, record.EmailCount);
        Assert.Equal(1, record.OptedOutCount);
        Assert.Equal(admin.Id, record.RequestedByUserId);
        Assert.Equal(admin.Email, record.RequestedByEmail);
        Assert.Equal(T0, record.RequestedAt);

        // Everyone in the audience gets the in-app notice — including the opted-out player.
        foreach (var user in new[] { player, optedOut, admin })
        {
            var inbox = await _notifications.ListAsync(user.Id, unreadOnly: false, take: 10, default);
            var notification = Assert.Single(inbox);
            Assert.Equal(AnnouncementAudiences.NotificationType, notification.Type);
            Assert.Equal("Dataset refreshed", notification.Title);
        }

        // The email respects the §9.9 opt-out and carries the campaign id.
        Assert.Equal(2, _emails.AnnouncementEmails.Count);
        Assert.DoesNotContain(_emails.AnnouncementEmails, email => email.Email == optedOut.Email);
        Assert.All(_emails.AnnouncementEmails, email => Assert.Equal(record.Id, email.Payload.CampaignId));

        // §9.10: the bulk email request is an audited admin action attributed to the actor.
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(SecurityAuditAction.AnnouncementSent, entry.Action);
        Assert.Equal(admin.Id, entry.UserId);
        Assert.Equal(admin.Email, entry.Email);
        Assert.Contains("Dataset refreshed", entry.Detail);
    }

    [Fact]
    public async Task Bulk_sends_are_throttled_into_staggered_delivery_windows()
    {
        for (var i = 0; i < 45; i++)
        {
            _identity.Add($"Player {i:00}");
        }

        var admin = _identity.Add("Admin", role: UserRole.Admin);

        await Send().Handle(
            new SendAnnouncementCommand(
                "Big news", "Bulk send", AnnouncementAudiences.All, null,
                ConfirmedRecipientCount: 46, admin.Id, admin.Email), default);

        Assert.Equal(46, _emails.AnnouncementEmails.Count);
        var windows = _emails.AnnouncementEmails
            .GroupBy(email => email.NotBefore)
            .OrderBy(group => group.Key)
            .ToArray();

        // 46 emails drain as 20 + 20 + 6, one 15-second window apart — never a single burst.
        Assert.Equal([T0, T0.AddSeconds(15), T0.AddSeconds(30)], windows.Select(group => group.Key));
        Assert.Equal([20, 20, 6], windows.Select(group => group.Count()));
    }

    [Fact]
    public async Task A_draft_audience_addresses_only_that_drafts_active_participants_and_deep_links_it()
    {
        var host = _identity.Add("Host");
        var guest = _identity.Add("Guest");
        _identity.Add("Bystander"); // active, but not in the draft — must not be addressed
        var admin = _identity.Add("Admin", role: UserRole.Admin);

        var templates = new FakeRosterTemplateService();
        var lifecycle = new DraftParticipantNotifier(_notifications, _emails, _identity);
        var created = await new CreateDraftCommandHandler(_drafts, templates, _identity, _runner, lifecycle)
            .Handle(new CreateDraftCommand("Friday Night", "1v1", host.Id, null, [guest.Id]), default);

        var preview = await Preview().Handle(
            new PreviewAnnouncementQuery("Ready?", "Kick-off soon.", AnnouncementAudiences.Draft, created.Summary.Id), default);
        Assert.Equal(2, preview.RecipientCount);
        Assert.Equal($"Participants of “Friday Night”", preview.AudienceLabel);

        var sent = await Send().Handle(
            new SendAnnouncementCommand(
                "Ready?", "Kick-off soon.", AnnouncementAudiences.Draft, created.Summary.Id,
                ConfirmedRecipientCount: 2, admin.Id, admin.Email), default);
        Assert.Equal(created.Summary.Id, sent.DraftId);

        // The in-app notice deep-links to the draft (PR-20 pipeline).
        var inbox = await _notifications.ListAsync(guest.Id, unreadOnly: false, take: 10, default);
        var announcement = inbox.Single(notification => notification.Type == AnnouncementAudiences.NotificationType);
        Assert.Equal(created.Summary.Id, announcement.DraftId);
    }

    [Fact]
    public async Task A_draft_audience_for_an_unknown_draft_is_not_found()
    {
        var admin = _identity.Add("Admin", role: UserRole.Admin);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Preview().Handle(
            new PreviewAnnouncementQuery("S", "B", AnnouncementAudiences.Draft, Guid.NewGuid()), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Send().Handle(
            new SendAnnouncementCommand(
                "S", "B", AnnouncementAudiences.Draft, Guid.NewGuid(), 1, admin.Id, admin.Email), default));
    }

    [Fact]
    public async Task A_long_body_is_truncated_for_the_in_app_notice_but_kept_whole_for_the_email()
    {
        var player = _identity.Add("Player");
        var admin = _identity.Add("Admin", role: UserRole.Admin);
        var body = new string('x', 1600);

        await Send().Handle(
            new SendAnnouncementCommand(
                "Long", body, AnnouncementAudiences.All, null,
                ConfirmedRecipientCount: 2, admin.Id, admin.Email), default);

        var inbox = await _notifications.ListAsync(player.Id, unreadOnly: false, take: 10, default);
        var notification = Assert.Single(inbox);
        Assert.True(notification.Body.Length <= 1024); // fits the user_notifications column
        Assert.EndsWith("…", notification.Body);

        var email = _emails.AnnouncementEmails.First();
        Assert.Equal(body, email.Payload.Body);
    }

    [Fact]
    public async Task The_in_memory_ledger_reports_per_campaign_delivery_tallies()
    {
        var campaign = Guid.NewGuid();
        _outbox.Record(EmailKind.Announcement, "a@draftroom.test", campaign, delivered: true, error: null);
        _outbox.Record(EmailKind.Announcement, "b@draftroom.test", campaign, delivered: false, error: "boom");
        _outbox.Record(EmailKind.Invitation, "c@draftroom.test", null, delivered: true, error: null);

        var tallies = Assert.Single(await _outbox.GetCampaignDeliveryAsync([campaign], default));
        Assert.Equal(campaign, tallies.CampaignId);
        Assert.Equal(0, tallies.Pending); // inline delivery leaves nothing pending
        Assert.Equal(1, tallies.Sent);
        Assert.Equal(1, tallies.Failed);
    }
}
