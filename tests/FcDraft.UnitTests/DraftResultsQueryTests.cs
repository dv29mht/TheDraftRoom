using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Drafts;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the PR-19 results read model (§9.7): available only for COMPLETED drafts and only to its
/// participants/host/admins (404-null otherwise); average and line ratings derive from the FROZEN picks
/// and slot positions; the pick sequence re-derives the exact acceptance order (straight held round, then
/// snake); and clubs/leagues/nations resolve from the pinned dataset as display-only extras.
/// </summary>
public sealed class DraftResultsQueryTests
{
    private readonly InMemoryDraftStore _store = new();
    private readonly InMemoryTransactionRunner _runner = new();
    private readonly FakeRosterTemplateService _templates = new();
    private readonly FakeDatasetAdminService _datasets = new();
    private readonly FakeIdentityDirectory _identity = new();
    private readonly ReversingShuffler _shuffler = new();
    private readonly FakeDraftCatalog _catalog = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 07, 16, 12, 00, 00, TimeSpan.Zero));
    private readonly IReadOnlyList<CatalogClub> _clubs;
    private readonly Guid _host;

    public DraftResultsQueryTests()
    {
        _host = _identity.Add("Host").Id;
        _clubs = _catalog.SeedStandardLeague();
    }

    private DraftExpiryService Expiry() => new(_store, _catalog, _identity, _runner, new NullDraftNotifier(), _clock, TestNotifiers.Lifecycle(_identity));

    private GetDraftResultsQueryHandler Results() => new(_store, _catalog, _identity);

    /// <summary>Drives a fresh 1v1 draft into the position draft (both teams clubbed + protected).</summary>
    private async Task<DraftDetail> PositionDraftAsync()
    {
        var guest = _identity.Add("Guest").Id;
        var create = new CreateDraftCommandHandler(_store, _templates, _identity, _runner, TestNotifiers.Lifecycle(_identity));
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

        var created = await create.Handle(new CreateDraftCommand("Draft", "1v1", _host, null, [guest]), default);
        var id = created.Summary.Id;
        var version = (await join.Handle(new JoinDraftCommand(id, created.Summary.Version, guest), default)).Summary.Version;
        version = (await @lock.Handle(new LockLobbyCommand(id, version, _host), default)).Summary.Version;
        version = (await formTeams.Handle(new FormTeamsCommand(id, null, version, _host), default)).Summary.Version;
        version = (await setReady.Handle(new SetReadyCommand(id, true, version, _host), default)).Summary.Version;
        version = (await setReady.Handle(new SetReadyCommand(id, true, version, guest), default)).Summary.Version;
        version = (await beginReady.Handle(new BeginReadyCheckCommand(id, version, _host), default)).Summary.Version;
        version = (await start.Handle(new StartDraftCommand(id, version, _host), default)).Version;
        version = (await spinner.Handle(new CommitSpinnerCommand(id, version, _host), default)).Summary.Version;
        var detail = await openClubs.Handle(new OpenClubSelectionCommand(id, version, _host), default);

        foreach (var rank in new[] { 1, 2 })
        {
            var team = detail.Teams.First(candidate => candidate.SpinnerRank == rank);
            var club = _clubs[rank - 1];
            var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(ClubId: club.Id), default))
                .First(candidate => detail.Picks.All(pick => pick.FootballerId != candidate.Id));
            detail = await select.Handle(new SelectClubAndProtectCommand(id, club.Id, footballer.Id, detail.Summary.Version, team.MemberUserIds[0]), default);
        }

        return await openPositions.Handle(new OpenPositionDraftCommand(id, detail.Summary.Version, _host), default);
    }

    /// <summary>Runs every position pick to completion (best available for the announced slot each turn).</summary>
    private async Task<DraftDetail> CompletedDraftAsync()
    {
        var pickHandler = new SubmitPickCommandHandler(_store, _identity, _catalog, _runner, Expiry(), _clock, TestNotifiers.Lifecycle(_identity));
        var detail = await PositionDraftAsync();
        while (detail.Summary.Status == "PositionDraft")
        {
            var turn = detail.Turn;
            var taken = detail.Picks.Select(pick => pick.FootballerId).ToHashSet();
            var position = turn.SlotAcceptsAnyPosition ? null : turn.ActiveSlotPosition;
            var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(Position: position), default))
                .First(candidate => !taken.Contains(candidate.Id));
            detail = await pickHandler.Handle(
                new SubmitPickCommand(detail.Summary.Id, footballer.Id, detail.Summary.Version, turn.ActiveTeamMemberUserIds[0]), default);
        }

        Assert.Equal("Completed", detail.Summary.Status);
        return detail;
    }

    [Fact]
    public async Task Results_exist_only_for_completed_drafts_and_their_participants()
    {
        var inProgress = await PositionDraftAsync();
        Assert.Null(await Results().Handle(new GetDraftResultsQuery(inProgress.Summary.Id, _host, false), default));

        var completed = await CompletedDraftAsync();
        Assert.NotNull(await Results().Handle(new GetDraftResultsQuery(completed.Summary.Id, _host, false), default));

        var outsider = _identity.Add("Outsider").Id;
        Assert.Null(await Results().Handle(new GetDraftResultsQuery(completed.Summary.Id, outsider, false), default));
        // An admin may open any completed draft's results.
        Assert.NotNull(await Results().Handle(new GetDraftResultsQuery(completed.Summary.Id, outsider, true), default));
    }

    [Fact]
    public async Task Ratings_lines_clubs_and_sequence_derive_from_the_frozen_picks()
    {
        var completed = await CompletedDraftAsync();
        var results = await Results().Handle(new GetDraftResultsQuery(completed.Summary.Id, _host, false), default);
        Assert.NotNull(results);

        // Every team result: 16 picks, an average that matches the frozen overalls exactly, and
        // 4-3-3 line ratings (1 GK / 4 DEF / 3 MID / 3 FWD from the frozen slot positions).
        Assert.Equal(2, results!.Teams.Count);
        foreach (var team in results.Teams)
        {
            Assert.Equal(16, team.Picks.Count);
            Assert.Equal(Math.Round(team.Picks.Average(pick => pick.FootballerOverall), 1), team.AverageOverall);

            var lines = team.LineRatings.ToDictionary(line => line.Line);
            Assert.Equal(1, lines["GK"].SlotCount);
            Assert.Equal(4, lines["DEF"].SlotCount);
            Assert.Equal(3, lines["MID"].SlotCount);
            Assert.Equal(3, lines["FWD"].SlotCount);
            Assert.All(lines.Values, line => Assert.Equal(line.SlotCount, line.Filled));
            var defenders = team.Picks.Where(pick => pick.SlotPosition is "LB" or "CB" or "RB").ToArray();
            Assert.Equal(Math.Round(defenders.Average(pick => pick.FootballerOverall), 1), lines["DEF"].Average);

            // Display extras resolved from the pinned catalog; the club name matches the held pick's club.
            Assert.NotEmpty(team.Clubs);
            Assert.Contains("Test League", team.Leagues);
            Assert.Contains("Testland", team.Nations);
            Assert.Equal(team.Picks.First(pick => pick.SlotOrder == 0).ClubName, team.SelectedClubName);

            // Member names resolve from the identity store (one solo member per 1v1 team).
            var memberName = Assert.Single(team.MemberNames);
            Assert.Contains(memberName, new[] { "Host", "Guest" });
        }

        // The global sequence is 1..32 in acceptance order: held round straight (rank 1 then 2), then
        // snake — odd rounds ascend, even rounds descend.
        Assert.Equal(Enumerable.Range(1, 32), results.PickSequence.Select(pick => pick.Sequence));
        var rankByTeam = results.Teams.ToDictionary(team => team.TeamId, team => team.SpinnerRank!.Value);
        var expectedRanks = new List<int> { 1, 2 };
        for (var round = 1; round <= 15; round++)
        {
            expectedRanks.AddRange(round % 2 == 1 ? [1, 2] : [2, 1]);
        }
        Assert.Equal(expectedRanks, results.PickSequence.Select(pick => rankByTeam[pick.TeamId]));
    }
}
