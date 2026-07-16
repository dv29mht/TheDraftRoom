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
/// Proves the PR-12/PR-13 done-whens against a real PostgreSQL server: 2v2 seeds and formed teams persist and
/// each participant lands on exactly one team (the unique <c>(draft_id, participant_id)</c> index); and a
/// committed spinner gives every team one unique rank (the unique <c>(draft_id, spinner_rank)</c> index) that
/// a retry cannot reshuffle. Tests share one database, so every assertion is scoped to the draft it created.
/// Skips cleanly when Docker is unavailable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DraftTeamFormationDbTests(PostgresFixture fixture)
{
    private static async Task<Guid> HostIdAsync(IServiceScope scope)
    {
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var host = await identity.FindByEmailAsync(SeededAccounts.PlayerEmail, default);
        return host!.Id;
    }

    private static Task<User> NewPlayerAsync(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IIdentityService>()
            .CreateUserAsync("Formation Player", $"formation-{Guid.NewGuid():N}@draftroom.test", UserRole.Player, default);

    [SkippableFact]
    public async Task Seeds_and_2v2_teams_persist_with_each_participant_on_one_team()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        Guid[] members;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var host = await HostIdAsync(scope);
            var g1 = (await NewPlayerAsync(scope)).Id;
            var g2 = (await NewPlayerAsync(scope)).Id;
            var g3 = (await NewPlayerAsync(scope)).Id;
            members = [host, g1, g2, g3];

            var created = await sender.Send(new CreateDraftCommand($"DB Formation {Guid.NewGuid():N}", "2v2", host, null, [g1, g2, g3]));
            draftId = created.Summary.Id;
            var version = created.Summary.Version;
            version = (await sender.Send(new LockLobbyCommand(draftId, version, host))).Summary.Version;

            var seeds = new[] { (host, "Seed1"), (g1, "Seed2"), (g2, "Seed1"), (g3, "Seed2") };
            foreach (var (userId, seed) in seeds)
            {
                version = (await sender.Send(new AssignSeedCommand(draftId, userId, seed, version, host))).Summary.Version;
            }

            var teams = new[]
            {
                new TeamFormationInput("Alpha", [host, g1]),
                new TeamFormationInput("Bravo", [g2, g3]),
            };
            await sender.Send(new FormTeamsCommand(draftId, teams, version, host));
        }

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts
                .Include(d => d.Participants)
                .Include(d => d.Teams).ThenInclude(team => team.Members)
                .Include(d => d.Events)
                .FirstAsync(d => d.Id == draftId);

            Assert.Equal(2, draft.Teams.Count);
            Assert.Equal(2, draft.Participants.Count(p => p.Seed == DraftSeed.Seed1));
            Assert.Equal(2, draft.Participants.Count(p => p.Seed == DraftSeed.Seed2));

            // Every participant is a member of exactly one team.
            var memberParticipantIds = draft.Teams.SelectMany(team => team.Members.Select(member => member.ParticipantId)).ToArray();
            Assert.Equal(members.Length, memberParticipantIds.Length);
            Assert.Equal(members.Length, memberParticipantIds.Distinct().Count());
            Assert.All(draft.Participants, participant => Assert.Contains(participant.Id, memberParticipantIds));

            Assert.Contains(draft.Events, e => e.Type == DraftEventType.ParticipantSeedAssigned);
            Assert.Contains(draft.Events, e => e.Type == DraftEventType.TeamsFormed);
        }
    }

    [SkippableFact]
    public async Task Committing_the_spinner_assigns_unique_ranks_that_a_retry_cannot_reshuffle()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var host = await HostIdAsync(scope);
            var guest = (await NewPlayerAsync(scope)).Id;

            var created = await sender.Send(new CreateDraftCommand($"DB Spinner {Guid.NewGuid():N}", "1v1", host, null, [guest]));
            draftId = created.Summary.Id;
            var version = created.Summary.Version;
            version = (await sender.Send(new JoinDraftCommand(draftId, version, guest))).Summary.Version;
            version = (await sender.Send(new LockLobbyCommand(draftId, version, host))).Summary.Version;
            version = (await sender.Send(new FormTeamsCommand(draftId, null, version, host))).Summary.Version;
            version = (await sender.Send(new SetReadyCommand(draftId, true, version, host))).Summary.Version;
            version = (await sender.Send(new SetReadyCommand(draftId, true, version, guest))).Summary.Version;
            version = (await sender.Send(new BeginReadyCheckCommand(draftId, version, host))).Summary.Version;
            version = (await sender.Send(new StartDraftCommand(draftId, version, host))).Version;

            var spun = await sender.Send(new CommitSpinnerCommand(draftId, version, host));
            var ranks = spun.Teams.Select(team => team.SpinnerRank).ToArray();
            Assert.Equal(new[] { 1, 2 }, ranks.Select(rank => rank!.Value).OrderBy(rank => rank).ToArray());

            // A retry at the now-current version is a no-op: same ranks, no reshuffle.
            var retry = await sender.Send(new CommitSpinnerCommand(draftId, spun.Summary.Version, host));
            Assert.Equal(spun.Summary.Version, retry.Summary.Version);
            Assert.Equal(
                spun.Teams.ToDictionary(team => team.Id, team => team.SpinnerRank),
                retry.Teams.ToDictionary(team => team.Id, team => team.SpinnerRank));
        }

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts.Include(d => d.Teams).Include(d => d.Events).FirstAsync(d => d.Id == draftId);

            var ranks = draft.Teams.Select(team => team.SpinnerRank).ToArray();
            Assert.All(ranks, rank => Assert.NotNull(rank));
            Assert.Equal(ranks.Length, ranks.Distinct().Count()); // unique ranks persisted
            Assert.Single(draft.Events, e => e.Type == DraftEventType.SpinnerOrderCommitted);
            Assert.Single(draft.Events, e => e.Type == DraftEventType.SpinnerOrderRevealed);
        }
    }
}
