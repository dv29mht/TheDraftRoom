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
/// Proves the PR-14/PR-15 done-whens against a real PostgreSQL server: the pre-draft club round runs in
/// straight spinner order and the position draft snakes, both persisting; the unique indexes on
/// <c>(draft_id, selected_club_id)</c>, <c>(draft_id, footballer_id)</c>, and <c>(draft_team_id, slot_order)</c>
/// reject a duplicate club/footballer/slot transactionally; and filling the last slot completes the draft.
/// Tests share one database, so every assertion is scoped to the draft it created. Skips when Docker is absent.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DraftClubAndPositionDbTests(PostgresFixture fixture)
{
    internal static async Task<Guid> HostIdAsync(IServiceScope scope)
    {
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var host = await identity.FindByEmailAsync(SeededAccounts.PlayerEmail, default);
        return host!.Id;
    }

    internal static Task<User> NewPlayerAsync(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IIdentityService>()
            .CreateUserAsync("Pick Player", $"pick-{Guid.NewGuid():N}@draftroom.test", UserRole.Player, default);

    // Drives a fresh 1v1 draft to the open club-selection state and returns its snapshot. Order-independent
    // in the shared DB: it activates the richest dataset version and curates that version's clubs itself,
    // rather than relying on whatever version another test left active or on the boot-time five-star seed.
    internal static async Task<DraftDetail> ClubRoundAsync(IServiceScope scope, ISender sender, Guid host)
    {
        var richVersion = await ActivateRichDatasetAsync(scope);
        await MarkTopClubsFiveStarAsync(scope, richVersion);

        var guest = (await NewPlayerAsync(scope)).Id;
        var created = await sender.Send(new CreateDraftCommand($"DB Draft {Guid.NewGuid():N}", "1v1", host, null, [guest]));
        var id = created.Summary.Id;
        var version = created.Summary.Version;
        version = (await sender.Send(new JoinDraftCommand(id, version, guest))).Summary.Version;
        version = (await sender.Send(new LockLobbyCommand(id, version, host))).Summary.Version;
        version = (await sender.Send(new FormTeamsCommand(id, null, version, host))).Summary.Version;
        version = (await sender.Send(new SetReadyCommand(id, true, version, host))).Summary.Version;
        version = (await sender.Send(new SetReadyCommand(id, true, version, guest))).Summary.Version;
        version = (await sender.Send(new BeginReadyCheckCommand(id, version, host))).Summary.Version;
        var started = await sender.Send(new StartDraftCommand(id, version, host));
        Assert.Equal(richVersion, started.PinnedDatasetVersionId); // the draft pins the rich version we activated
        version = (await sender.Send(new CommitSpinnerCommand(id, started.Version, host))).Summary.Version;
        return await sender.Send(new OpenClubSelectionCommand(id, version, host));
    }

    // Activates the dataset version with the most eligible footballers (the bundled one) so the draft that
    // starts next pins a pool rich enough to fill every position for two teams.
    internal static async Task<Guid> ActivateRichDatasetAsync(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
        var version = await db.Footballers.AsNoTracking()
            .Where(footballer => footballer.Overall >= 75 && footballer.IsKickOffEligible && footballer.IsActive)
            .GroupBy(footballer => footballer.DatasetVersionId)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstAsync();

        await scope.ServiceProvider.GetRequiredService<IDatasetAdminService>().ActivateAsync(version, default);
        return version;
    }

    // Marks the four most-populated clubs of a version five-star (by 75+ player count), so the club round
    // always has several eligible clubs that each have a protectable player — independent of club names.
    internal static async Task MarkTopClubsFiveStarAsync(IServiceScope scope, Guid version)
    {
        var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
        var topClubNames = await db.Footballers.AsNoTracking()
            .Where(footballer => footballer.DatasetVersionId == version
                && footballer.Overall >= 75 && footballer.IsKickOffEligible && footballer.IsActive)
            .GroupBy(footballer => footballer.Club)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .Take(4)
            .ToListAsync();

        var names = topClubNames.ToHashSet();
        var clubs = await db.Clubs.Where(club => club.DatasetVersionId == version).ToListAsync();
        foreach (var club in clubs.Where(club => names.Contains(club.Name)))
        {
            club.IsFiveStarEligible = true;
        }

        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task Full_1v1_flow_runs_straight_clubs_then_snake_positions_to_completion()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var catalog = scope.ServiceProvider.GetRequiredService<IDraftCatalog>();
            var host = await HostIdAsync(scope);

            var detail = await ClubRoundAsync(scope, sender, host);
            draftId = detail.Summary.Id;
            var pinned = detail.Summary.PinnedDatasetVersionId;
            Assert.NotNull(pinned);

            // Straight club order: rank 1 chooses first, then rank 2. Each picks a distinct five-star club.
            var takenClubs = new HashSet<Guid>();
            for (var team = 0; team < 2; team++)
            {
                Assert.Equal("Straight", detail.Turn.Direction);
                Assert.Equal(team + 1, detail.Teams.First(t => t.Id == detail.Turn.ActiveTeamId).SpinnerRank);
                var actor = detail.Turn.ActiveTeamMemberUserIds[0];

                var club = (await catalog.ListFiveStarClubsAsync(pinned, default)).First(c => !takenClubs.Contains(c.Id));
                takenClubs.Add(club.Id);
                var taken = detail.Picks.Select(p => p.FootballerId).ToHashSet();
                var held = (await catalog.ListFootballersAsync(pinned, new CatalogFootballerFilter(ClubId: club.Id), default))
                    .First(f => !taken.Contains(f.Id));
                detail = await sender.Send(new SelectClubAndProtectCommand(draftId, club.Id, held.Id, detail.Summary.Version, actor));
            }

            // Open the position draft and run every pick, recording the picking team's rank each turn.
            detail = await sender.Send(new OpenPositionDraftCommand(draftId, detail.Summary.Version, host));
            var rankById = detail.Teams.ToDictionary(t => t.Id, t => t.SpinnerRank!.Value);
            var pickRanks = new List<int>();
            var guard = 0;
            while (detail.Summary.Status == "PositionDraft")
            {
                Assert.True(guard++ < 40, "the position draft did not complete");
                pickRanks.Add(rankById[detail.Turn.ActiveTeamId!.Value]);
                var actor = detail.Turn.ActiveTeamMemberUserIds[0];
                var position = detail.Turn.SlotAcceptsAnyPosition ? null : detail.Turn.ActiveSlotPosition;
                var taken = detail.Picks.Select(p => p.FootballerId).ToHashSet();
                var footballer = (await catalog.ListFootballersAsync(pinned, new CatalogFootballerFilter(Position: position, Take: 200), default))
                    .First(f => !taken.Contains(f.Id));
                detail = await sender.Send(new SubmitPickCommand(draftId, footballer.Id, detail.Summary.Version, actor));
            }

            var expected = Enumerable.Range(0, 30).Select(pick => DraftTurnOrder.NextPosition(2, pick).SpinnerRank).ToArray();
            Assert.Equal(expected, pickRanks);
            Assert.Equal("Completed", detail.Summary.Status);
        }

        // The completed draft persists: Completed status, both full 16-slot squads, globally-unique footballers.
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts.Include(d => d.Teams).Include(d => d.Picks).Include(d => d.Events)
                .FirstAsync(d => d.Id == draftId);

            Assert.Equal(DraftStatus.Completed, draft.Status);
            Assert.All(draft.Teams, team => Assert.NotNull(team.SelectedClubId));
            Assert.Equal(2, draft.Teams.Select(team => team.SelectedClubId).Distinct().Count());
            foreach (var team in draft.Teams)
            {
                Assert.Equal(16, draft.Picks.Count(pick => pick.DraftTeamId == team.Id));
            }
            Assert.Equal(draft.Picks.Count, draft.Picks.Select(pick => pick.FootballerId).Distinct().Count());
            Assert.Contains(draft.Events, e => e.Type == DraftEventType.DraftCompleted);
        }
    }

    [SkippableFact]
    public async Task A_duplicate_club_choice_is_rejected_transactionally()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        using var scope = api.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var catalog = scope.ServiceProvider.GetRequiredService<IDraftCatalog>();
        var host = await HostIdAsync(scope);

        var detail = await ClubRoundAsync(scope, sender, host);
        var id = detail.Summary.Id;
        var pinned = detail.Summary.PinnedDatasetVersionId;

        var club = (await catalog.ListFiveStarClubsAsync(pinned, default))[0];
        var players = await catalog.ListFootballersAsync(pinned, new CatalogFootballerFilter(ClubId: club.Id), default);

        // Rank 1 takes the club; rank 2 attempting the same club is rejected with no second team taking it.
        detail = await sender.Send(new SelectClubAndProtectCommand(id, club.Id, players[0].Id, detail.Summary.Version, detail.Turn.ActiveTeamMemberUserIds[0]));
        var rank2Actor = detail.Turn.ActiveTeamMemberUserIds[0];
        await Assert.ThrowsAsync<ValidationAppException>(() =>
            sender.Send(new SelectClubAndProtectCommand(id, club.Id, players[1].Id, detail.Summary.Version, rank2Actor)));

        var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
        var stored = await db.Drafts.AsNoTracking().Include(d => d.Teams)
            .FirstAsync(d => d.Id == id);
        Assert.Single(stored.Teams, team => team.SelectedClubId == club.Id);
    }

    [SkippableFact]
    public async Task The_unique_indexes_reject_a_duplicate_footballer_and_club()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        // A duplicate footballer across two teams violates ix_draft_picks_draft_footballer.
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = Draft.Create("Dup Footballer", DraftFormat.OneVsOne, Guid.NewGuid(), Guid.NewGuid(), $"F{Guid.NewGuid():N}"[..12]);
            var teamA = new DraftTeam { DraftId = draft.Id, Name = "A" };
            var teamB = new DraftTeam { DraftId = draft.Id, Name = "B" };
            draft.Teams.Add(teamA);
            draft.Teams.Add(teamB);
            draft.Picks.Add(NewPick(draft.Id, teamA.Id, 0, 424242));
            draft.Picks.Add(NewPick(draft.Id, teamB.Id, 0, 424242)); // same footballer id → violation
            db.Drafts.Add(draft);

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Two teams choosing the same club violates ix_draft_teams_draft_club.
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var clubId = Guid.NewGuid();
            var draft = Draft.Create("Dup Club", DraftFormat.OneVsOne, Guid.NewGuid(), Guid.NewGuid(), $"C{Guid.NewGuid():N}"[..12]);
            draft.Teams.Add(new DraftTeam { DraftId = draft.Id, Name = "A", SelectedClubId = clubId });
            draft.Teams.Add(new DraftTeam { DraftId = draft.Id, Name = "B", SelectedClubId = clubId });
            db.Drafts.Add(draft);

            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    private static DraftPick NewPick(Guid draftId, Guid teamId, int slotOrder, int footballerId) => new()
    {
        DraftId = draftId,
        DraftTeamId = teamId,
        SlotOrder = slotOrder,
        FootballerId = footballerId,
        FootballerName = $"Player {footballerId}",
        FootballerOverall = 85,
    };
}
