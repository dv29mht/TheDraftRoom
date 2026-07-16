using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Application.Features.Notifications;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Proves the PR-20 done-when against real PostgreSQL: per-user notifications are appended in the SAME
/// transaction as the draft mutation (a cancellation leaves notification rows AND durable outbox email
/// rows), survive an API "restart" (a brand-new host over the same database), stay authorization-scoped,
/// and deep-link to their draft. Tests share one database — every assertion is scoped to what it created.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UserNotificationDbTests(PostgresFixture fixture)
{
    [SkippableFact]
    public async Task Notifications_survive_a_restart_and_mark_read_persists()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        Guid draftId;
        Guid guestId;
        Guid notificationId;

        // Host 1: invite a fresh player into a new lobby, then mark the landed notification read.
        await using (var api = new PostgresApiFactory(fixture.ConnectionString!, useEmailOutbox: true))
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var host = await DraftClubAndPositionDbTests.HostIdAsync(scope);
            guestId = (await DraftClubAndPositionDbTests.NewPlayerAsync(scope)).Id;

            var created = await sender.Send(new CreateDraftCommand($"Notify {Guid.NewGuid():N}"[..20], "1v1", host, null, [guestId]));
            draftId = created.Summary.Id;

            var inbox = await sender.Send(new ListMyNotificationsQuery(guestId));
            var invite = Assert.Single(inbox.Items, item => item.DraftId == draftId);
            Assert.Equal(DraftParticipantNotifier.InvitedType, invite.Type);
            notificationId = invite.Id;
            await sender.Send(new MarkNotificationReadCommand(notificationId, guestId));
        }

        // Host 2 (a fresh application over the same database): the notification and its read stamp survive.
        await using (var api = new PostgresApiFactory(fixture.ConnectionString!, useEmailOutbox: true))
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var inbox = await sender.Send(new ListMyNotificationsQuery(guestId));
            var survived = Assert.Single(inbox.Items, item => item.Id == notificationId);
            Assert.NotNull(survived.ReadAt);
            Assert.Equal(draftId, survived.DraftId); // the deep link survives too
            Assert.Equal(0, inbox.UnreadCount);

            // Authorization-scoped: another account cannot mark (or discover) it.
            var stranger = (await DraftClubAndPositionDbTests.NewPlayerAsync(scope)).Id;
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                sender.Send(new MarkNotificationReadCommand(notificationId, stranger)));
        }
    }

    [SkippableFact]
    public async Task A_cancellation_commits_notification_rows_and_outbox_emails_together()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!, useEmailOutbox: true);

        using var scope = api.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var host = await DraftClubAndPositionDbTests.HostIdAsync(scope);
        var guest = await DraftClubAndPositionDbTests.NewPlayerAsync(scope);

        var created = await sender.Send(new CreateDraftCommand($"Cancel {Guid.NewGuid():N}"[..20], "1v1", host, null, [guest.Id]));
        await sender.Send(new CancelDraftCommand(created.Summary.Id, "DB proof", created.Summary.Version, host));

        var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();

        // Both participants got a persistent cancellation notification pointing at this draft…
        var notifications = await db.UserNotifications.AsNoTracking()
            .Where(notification => notification.DraftId == created.Summary.Id
                && notification.Type == DraftParticipantNotifier.CancelledType)
            .ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, notification => notification.UserId == guest.Id);

        // …and the durable outbox holds their emails, committed by the same transaction (never inline Brevo).
        var outbox = await db.EmailOutbox.AsNoTracking()
            .Where(message => message.Kind == EmailKind.DraftCancelled && message.Payload!.Contains(created.Summary.Id.ToString()))
            .ToListAsync();
        Assert.Equal(2, outbox.Count);
        Assert.Contains(outbox, message => message.ToEmail == guest.Email);
    }
}
