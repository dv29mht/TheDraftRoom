using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Audit;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Auth;
using FcDraft.Infrastructure.Drafts;
using FcDraft.Infrastructure.Notifications;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the PR-21 §9.10 audit views over the in-memory stores: the draft-event query filters
/// (draft, user, type, date) with actor attribution, and the security/admin-event query filters
/// (action, user, email, date). Both readers are read-only by construction — there is no mutation
/// member anywhere on their seams to misuse.
/// </summary>
public sealed class AuditQueryTests
{
    private readonly FakeIdentityDirectory _identity = new();
    private readonly InMemoryDraftStore _drafts = new();
    private readonly InMemoryTransactionRunner _runner = new();
    private readonly InMemoryUserNotificationStore _notifications = new();
    private readonly RecordingEmailQueue _emails = new();

    private QueryDraftEventsQueryHandler DraftEvents() =>
        new(new InMemoryDraftEventReader(_drafts), _identity);

    private async Task<(Guid DraftId, User Host, User Guest)> SeedDraftAsync()
    {
        var host = _identity.Add("Host");
        var guest = _identity.Add("Guest");
        var lifecycle = new DraftParticipantNotifier(_notifications, _emails, _identity);
        var created = await new CreateDraftCommandHandler(
                _drafts, new FakeRosterTemplateService(), _identity, _runner, lifecycle)
            .Handle(new CreateDraftCommand("Audit Trail", "1v1", host.Id, null, [guest.Id]), default);
        return (created.Summary.Id, host, guest);
    }

    [Fact]
    public async Task Draft_events_filter_by_draft_type_and_actor_with_resolved_names()
    {
        var (draftId, host, _) = await SeedDraftAsync();
        await SeedDraftAsync(); // a second draft proves the draft filter isolates one trail

        var all = await DraftEvents().Handle(new QueryDraftEventsQuery(DraftId: draftId), default);
        Assert.All(all, record => Assert.Equal(draftId, record.DraftId));
        Assert.Contains(all, record => record.Type == nameof(DraftEventType.DraftCreated));
        Assert.Contains(all, record => record.Type == nameof(DraftEventType.ParticipantInvited));

        var created = await DraftEvents().Handle(
            new QueryDraftEventsQuery(DraftId: draftId, Type: "draftcreated"), default); // case-insensitive
        var creation = Assert.Single(created);
        Assert.Equal(host.Id, creation.ActorUserId);
        Assert.Equal(host.DisplayName, creation.ActorName); // attribution resolves the actor's name

        var byActor = await DraftEvents().Handle(
            new QueryDraftEventsQuery(ActorUserId: host.Id), default);
        Assert.NotEmpty(byActor);
        Assert.All(byActor, record => Assert.Equal(host.Id, record.ActorUserId));
    }

    [Fact]
    public async Task Draft_events_are_newest_first_and_capped_by_take()
    {
        var (draftId, _, _) = await SeedDraftAsync();

        var limited = await DraftEvents().Handle(
            new QueryDraftEventsQuery(DraftId: draftId, Take: 1), default);
        var newest = Assert.Single(limited);

        var all = await DraftEvents().Handle(new QueryDraftEventsQuery(DraftId: draftId), default);
        Assert.Equal(all.Max(record => record.Sequence), newest.Sequence);
    }

    [Fact]
    public async Task An_unknown_draft_event_type_is_a_validation_error()
    {
        await Assert.ThrowsAsync<ValidationAppException>(() =>
            DraftEvents().Handle(new QueryDraftEventsQuery(Type: "NotAnEvent"), default));
    }

    [Fact]
    public async Task Security_events_filter_by_action_email_user_and_date()
    {
        var service = new InMemorySecurityAuditService();
        var adminId = Guid.NewGuid();
        await service.RecordAsync(new SecurityAuditEntry(
            SecurityAuditAction.SignInSucceeded, Email: "player@draftroom.test"), default);
        await service.RecordAsync(new SecurityAuditEntry(
            SecurityAuditAction.AnnouncementSent, UserId: adminId, Email: "admin@draftroom.test",
            Detail: "“Hello” to All active players"), default);

        var handler = new QuerySecurityEventsQueryHandler(service);

        var byAction = await handler.Handle(
            new QuerySecurityEventsQuery(Action: nameof(SecurityAuditAction.AnnouncementSent)), default);
        var announcement = Assert.Single(byAction);
        Assert.Equal(adminId, announcement.UserId);

        var byEmail = await handler.Handle(new QuerySecurityEventsQuery(Email: "ADMIN@"), default);
        Assert.Single(byEmail); // substring, case-insensitive

        var byUser = await handler.Handle(new QuerySecurityEventsQuery(UserId: adminId), default);
        Assert.Single(byUser);

        var future = await handler.Handle(
            new QuerySecurityEventsQuery(From: DateTimeOffset.UtcNow.AddDays(1)), default);
        Assert.Empty(future);

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            handler.Handle(new QuerySecurityEventsQuery(Action: "NotAnAction"), default));
    }
}
