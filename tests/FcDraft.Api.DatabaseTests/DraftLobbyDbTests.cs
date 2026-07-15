using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Proves the PR-11 done-when against a real PostgreSQL server: a lobby and its attendance persist and can
/// be reopened; the capacity rules (1v1 2–10, 2v2 4–16 even) are enforced server-side when locking; and a
/// deactivated account cannot be invited. Tests share one database, so every assertion is scoped to the
/// draft/user it created. Skips cleanly when Docker is unavailable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DraftLobbyDbTests(PostgresFixture fixture)
{
    private static async Task<Guid> HostIdAsync(IServiceScope scope)
    {
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var host = await identity.FindByEmailAsync(SeededAccounts.PlayerEmail, default);
        return host!.Id;
    }

    private static Task<User> NewPlayerAsync(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IIdentityService>()
            .CreateUserAsync("Invitee", $"invitee-{Guid.NewGuid():N}@draftroom.test", UserRole.Player, default);

    [SkippableFact]
    public async Task A_lobby_and_its_attendance_persist_and_can_be_reopened()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        Guid inviteeId;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var host = await HostIdAsync(scope);
            inviteeId = (await NewPlayerAsync(scope)).Id;

            var created = await sender.Send(new CreateDraftCommand($"DB Lobby {Guid.NewGuid():N}", "1v1", host, null, [inviteeId]));
            draftId = created.Summary.Id;
            Assert.Equal(2, created.Participants.Count);

            await sender.Send(new JoinDraftCommand(draftId, created.Summary.Version, inviteeId));
        }

        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var reopened = await sender.Send(new GetDraftQuery(draftId));
            Assert.NotNull(reopened);
            Assert.Contains(reopened!.Participants, p => p.IsHost && p.Status == "Joined");
            Assert.Contains(reopened.Participants, p => p.UserId == inviteeId && p.Status == "Joined");

            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts.Include(d => d.Participants).Include(d => d.Events).FirstAsync(d => d.Id == draftId);
            Assert.Equal(2, draft.Participants.Count);
            Assert.Contains(draft.Events, e => e.Type == DraftEventType.ParticipantInvited);
            Assert.Contains(draft.Events, e => e.Type == DraftEventType.ParticipantJoined);
        }
    }

    [SkippableFact]
    public async Task Capacity_rules_are_enforced_server_side_when_locking()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        using var scope = api.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var host = await HostIdAsync(scope);

        // 1v1 with only the host (1) is below the minimum of 2.
        var solo = await sender.Send(new CreateDraftCommand($"DB Solo {Guid.NewGuid():N}", "1v1", host));
        await Assert.ThrowsAsync<ValidationAppException>(() =>
            sender.Send(new LockLobbyCommand(solo.Summary.Id, solo.Summary.Version, host)));

        // 2v2 with an odd count (host + 1 = 2, below 4) cannot lock.
        var oddId = (await NewPlayerAsync(scope)).Id;
        var odd = await sender.Send(new CreateDraftCommand($"DB Odd {Guid.NewGuid():N}", "2v2", host, null, [oddId]));
        await Assert.ThrowsAsync<ValidationAppException>(() =>
            sender.Send(new LockLobbyCommand(odd.Summary.Id, odd.Summary.Version, host)));

        // A valid even 2v2 (host + 3 = 4) locks into team formation.
        var invitees = new[] { (await NewPlayerAsync(scope)).Id, (await NewPlayerAsync(scope)).Id, (await NewPlayerAsync(scope)).Id };
        var valid = await sender.Send(new CreateDraftCommand($"DB Valid {Guid.NewGuid():N}", "2v2", host, null, invitees));
        var locked = await sender.Send(new LockLobbyCommand(valid.Summary.Id, valid.Summary.Version, host));
        Assert.Equal("TeamFormation", locked.Summary.Status);
    }

    [SkippableFact]
    public async Task A_deactivated_user_cannot_be_invited()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        using var scope = api.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var host = await HostIdAsync(scope);

        var deactivated = await NewPlayerAsync(scope);
        await identity.SetUserStatusAsync(deactivated.Id, AccountStatus.Deactivated, default);

        var lobby = await sender.Send(new CreateDraftCommand($"DB Reject {Guid.NewGuid():N}", "1v1", host));

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            sender.Send(new InviteParticipantCommand(lobby.Summary.Id, deactivated.Id, lobby.Summary.Version, host)));
    }
}
