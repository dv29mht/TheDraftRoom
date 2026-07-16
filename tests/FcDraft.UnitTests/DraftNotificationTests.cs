using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Application.Features.Notifications;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Drafts;
using FcDraft.Infrastructure.Notifications;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the PR-20 participant communications: draft lifecycle moments append per-user notification rows
/// and outbox emails inside the mutating transaction; the reminder honours the §9.9 opt-out for EMAIL only
/// (essential messages ignore it, in-app notices always land); and the read endpoints are authorization
/// scoped — another user's notification id behaves as missing.
/// </summary>
public sealed class DraftNotificationTests
{
    private readonly InMemoryDraftStore _store = new();
    private readonly InMemoryTransactionRunner _runner = new();
    private readonly FakeRosterTemplateService _templates = new();
    private readonly FakeDatasetAdminService _datasets = new();
    private readonly FakeIdentityDirectory _identity = new();
    private readonly ReversingShuffler _shuffler = new();
    private readonly FakeDraftCatalog _catalog = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 07, 16, 12, 00, 00, TimeSpan.Zero));
    private readonly InMemoryUserNotificationStore _notifications = new();
    private readonly RecordingEmailQueue _emails = new();
    private readonly Guid _host;

    public DraftNotificationTests()
    {
        _host = _identity.Add("Host").Id;
        _catalog.SeedStandardLeague();
    }

    private DraftParticipantNotifier Lifecycle() => new(_notifications, _emails, _identity);

    private CreateDraftCommandHandler Create() => new(_store, _templates, _identity, _runner, Lifecycle());

    private DraftExpiryService Expiry() =>
        new(_store, _catalog, _identity, _runner, new NullDraftNotifier(), _clock, Lifecycle());

    [Fact]
    public async Task An_invite_notifies_the_invitee_in_app_and_by_outbox_email()
    {
        var guest = _identity.Add("Guest");
        var created = await Create().Handle(new CreateDraftCommand("Tuesday", "1v1", _host, null, [guest.Id]), default);

        var inbox = await _notifications.ListAsync(guest.Id, unreadOnly: false, take: 10, default);
        var notification = Assert.Single(inbox);
        Assert.Equal(DraftParticipantNotifier.InvitedType, notification.Type);
        Assert.Equal(created.Summary.Id, notification.DraftId); // deep-links to the draft
        Assert.Null(notification.ReadAt);

        var email = Assert.Single(_emails.DraftEmails);
        Assert.Equal(EmailKind.DraftInvitation, email.Kind);
        Assert.Equal(guest.Email, email.Email);
        Assert.Equal(created.Summary.Id, email.Payload.DraftId);

        // The host invited themselves into nothing — no notification for the actor.
        Assert.Empty(await _notifications.ListAsync(_host, false, 10, default));
    }

    [Fact]
    public async Task A_cancellation_notifies_every_participant_with_the_reason()
    {
        var guest = _identity.Add("Guest");
        var created = await Create().Handle(new CreateDraftCommand("Doomed", "1v1", _host, null, [guest.Id]), default);
        var cancel = new CancelDraftCommandHandler(_store, _identity, _catalog, _runner, _clock, Lifecycle());

        await cancel.Handle(new CancelDraftCommand(created.Summary.Id, "Venue lost", created.Summary.Version, _host), default);

        foreach (var userId in new[] { _host, guest.Id })
        {
            var cancelled = (await _notifications.ListAsync(userId, false, 10, default))
                .Single(notification => notification.Type == DraftParticipantNotifier.CancelledType);
            Assert.Contains("Venue lost", cancelled.Body);
            Assert.Equal(created.Summary.Id, cancelled.DraftId);
        }

        Assert.Equal(2, _emails.DraftEmails.Count(email => email.Kind == EmailKind.DraftCancelled));
        Assert.All(_emails.DraftEmails.Where(email => email.Kind == EmailKind.DraftCancelled),
            email => Assert.Equal("Venue lost", email.Payload.Reason));
    }

    [Fact]
    public async Task The_reminder_skips_the_actor_and_honours_the_email_opt_out_but_not_in_app()
    {
        var guest = _identity.Add("Guest");
        var optedOut = _identity.Add("Quiet");
        await _identity.SetOptionalEmailOptOutAsync(optedOut.Id, true, default);
        var created = await Create().Handle(new CreateDraftCommand("Nudge", "1v1", _host, null, [guest.Id, optedOut.Id]), default);
        var remind = new SendDraftReminderCommandHandler(_store, _runner, Lifecycle());

        var reminded = await remind.Handle(new SendDraftReminderCommand(created.Summary.Id, _host), default);

        Assert.Equal(2, reminded); // guest + opted-out — never the actor
        Assert.DoesNotContain(
            await _notifications.ListAsync(_host, false, 20, default),
            notification => notification.Type == DraftParticipantNotifier.ReminderType);
        Assert.Single(
            await _notifications.ListAsync(optedOut.Id, false, 20, default),
            notification => notification.Type == DraftParticipantNotifier.ReminderType); // in-app ALWAYS lands

        var reminderEmails = _emails.DraftEmails.Where(email => email.Kind == EmailKind.DraftReminder).ToArray();
        var reminderEmail = Assert.Single(reminderEmails); // only the guest — the opt-out held
        Assert.Equal(guest.Email, reminderEmail.Email);
    }

    [Fact]
    public async Task A_reminder_is_rejected_once_the_draft_has_started()
    {
        var guest = _identity.Add("Guest");
        var created = await Create().Handle(new CreateDraftCommand("Started", "1v1", _host, null, [guest.Id]), default);
        var join = new JoinDraftCommandHandler(_store, _identity, _runner);
        var @lock = new LockLobbyCommandHandler(_store, _identity, _runner);
        var version = (await join.Handle(new JoinDraftCommand(created.Summary.Id, created.Summary.Version, guest.Id), default)).Summary.Version;
        await @lock.Handle(new LockLobbyCommand(created.Summary.Id, version, _host), default);
        var formTeams = new FormTeamsCommandHandler(_store, _identity, _runner);
        var detail = await formTeams.Handle(new FormTeamsCommand(created.Summary.Id, null, version + 1, _host), default);

        // TeamFormation still allows a reminder…
        var remind = new SendDraftReminderCommandHandler(_store, _runner, Lifecycle());
        Assert.Equal(1, await remind.Handle(new SendDraftReminderCommand(created.Summary.Id, _host), default));

        // …and only the host (or an admin) may send one.
        await Assert.ThrowsAsync<FcDraft.Application.Common.Exceptions.ForbiddenAppException>(() =>
            remind.Handle(new SendDraftReminderCommand(detail.Summary.Id, guest.Id), default));
    }

    [Fact]
    public async Task Mark_read_is_scoped_to_the_owner_and_drives_the_unread_count()
    {
        var guest = _identity.Add("Guest");
        await Create().Handle(new CreateDraftCommand("Inbox", "1v1", _host, null, [guest.Id]), default);
        var notification = Assert.Single(await _notifications.ListAsync(guest.Id, false, 10, default));

        var markRead = new MarkNotificationReadCommandHandler(_notifications, _runner, _clock);

        // Another user's notification id behaves exactly like a missing one (404, not 403).
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            markRead.Handle(new MarkNotificationReadCommand(notification.Id, _host), default));

        var afterRead = await markRead.Handle(new MarkNotificationReadCommand(notification.Id, guest.Id), default);
        Assert.Equal(0, afterRead.UnreadCount);
        Assert.NotNull(afterRead.Items.Single().ReadAt);

        // Mark-all covers a refilled inbox.
        await Create().Handle(new CreateDraftCommand("Inbox 2", "1v1", _host, null, [guest.Id]), default);
        var list = new ListMyNotificationsQueryHandler(_notifications);
        Assert.Equal(1, (await list.Handle(new ListMyNotificationsQuery(guest.Id), default)).UnreadCount);
        var markAll = new MarkAllNotificationsReadCommandHandler(_notifications, _runner, _clock);
        Assert.Equal(0, (await markAll.Handle(new MarkAllNotificationsReadCommand(guest.Id), default)).UnreadCount);
    }

    [Fact]
    public async Task Completing_the_final_pick_notifies_every_participant_with_the_results()
    {
        // Drive a full 1v1 to completion through the real handlers.
        var guest = _identity.Add("Guest");
        var join = new JoinDraftCommandHandler(_store, _identity, _runner);
        var @lock = new LockLobbyCommandHandler(_store, _identity, _runner);
        var formTeams = new FormTeamsCommandHandler(_store, _identity, _runner);
        var setReady = new SetReadyCommandHandler(_store, _identity, _runner);
        var beginReady = new BeginReadyCheckCommandHandler(_store, _identity, _runner);
        var start = new StartDraftCommandHandler(_store, _templates, _datasets, _runner);
        var spinner = new CommitSpinnerCommandHandler(_store, _identity, _shuffler, _runner);
        var openClubs = new OpenClubSelectionCommandHandler(_store, _identity, _catalog, _runner);
        var select = new SelectClubAndProtectCommandHandler(_store, _identity, _catalog, _runner);
        var openPositions = new OpenPositionDraftCommandHandler(_store, _identity, _catalog, _runner, _clock);
        var pick = new SubmitPickCommandHandler(_store, _identity, _catalog, _runner, Expiry(), _clock, Lifecycle());

        var created = await Create().Handle(new CreateDraftCommand("Full", "1v1", _host, null, [guest.Id]), default);
        var id = created.Summary.Id;
        var version = (await join.Handle(new JoinDraftCommand(id, created.Summary.Version, guest.Id), default)).Summary.Version;
        version = (await @lock.Handle(new LockLobbyCommand(id, version, _host), default)).Summary.Version;
        version = (await formTeams.Handle(new FormTeamsCommand(id, null, version, _host), default)).Summary.Version;
        version = (await setReady.Handle(new SetReadyCommand(id, true, version, _host), default)).Summary.Version;
        version = (await setReady.Handle(new SetReadyCommand(id, true, version, guest.Id), default)).Summary.Version;
        version = (await beginReady.Handle(new BeginReadyCheckCommand(id, version, _host), default)).Summary.Version;
        version = (await start.Handle(new StartDraftCommand(id, version, _host), default)).Version;
        version = (await spinner.Handle(new CommitSpinnerCommand(id, version, _host), default)).Summary.Version;
        var detail = await openClubs.Handle(new OpenClubSelectionCommand(id, version, _host), default);
        var clubs = await _catalog.ListFiveStarClubsAsync(null, default);
        foreach (var rank in new[] { 1, 2 })
        {
            var team = detail.Teams.First(candidate => candidate.SpinnerRank == rank);
            var taken = detail.Picks.Select(p => p.FootballerId).ToHashSet();
            var held = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(ClubId: clubs[rank - 1].Id), default))
                .First(candidate => !taken.Contains(candidate.Id));
            detail = await select.Handle(new SelectClubAndProtectCommand(id, clubs[rank - 1].Id, held.Id, detail.Summary.Version, team.MemberUserIds[0]), default);
        }
        detail = await openPositions.Handle(new OpenPositionDraftCommand(id, detail.Summary.Version, _host), default);
        while (detail.Summary.Status == "PositionDraft")
        {
            var taken = detail.Picks.Select(p => p.FootballerId).ToHashSet();
            var position = detail.Turn.SlotAcceptsAnyPosition ? null : detail.Turn.ActiveSlotPosition;
            var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(Position: position), default))
                .First(candidate => !taken.Contains(candidate.Id));
            detail = await pick.Handle(new SubmitPickCommand(id, footballer.Id, detail.Summary.Version, detail.Turn.ActiveTeamMemberUserIds[0]), default);
        }

        Assert.Equal("Completed", detail.Summary.Status);
        foreach (var userId in new[] { _host, guest.Id })
        {
            var completed = (await _notifications.ListAsync(userId, false, 20, default))
                .Single(notification => notification.Type == DraftParticipantNotifier.CompletedType);
            Assert.Equal(id, completed.DraftId);
        }

        Assert.Equal(2, _emails.DraftEmails.Count(email => email.Kind == EmailKind.DraftCompleted));
    }
}
