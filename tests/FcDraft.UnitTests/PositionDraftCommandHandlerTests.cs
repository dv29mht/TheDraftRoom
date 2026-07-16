using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Datasets;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Drafts;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the PR-15 position draft over the in-memory store: picks advance ST → … → GK → 4 subs in
/// <b>snake</b> order over committed spinner ranks; a pick must match the slot's position and still be
/// available; out-of-turn, duplicate, ineligible, and wrong-state picks are rejected; and filling the final
/// slot completes the draft.
/// </summary>
public sealed class PositionDraftCommandHandlerTests
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

    public PositionDraftCommandHandlerTests()
    {
        _host = _identity.Add("Host").Id;
        _clubs = _catalog.SeedStandardLeague();
    }

    private DraftExpiryService Expiry() => new(_store, _catalog, _identity, _runner, new NullDraftNotifier(), _clock, TestNotifiers.Lifecycle(_identity));

    private SubmitPickCommandHandler Pick() => new(_store, _identity, _catalog, _runner, Expiry(), _clock, TestNotifiers.Lifecycle(_identity));

    /// <summary>Drives a 1v1 draft all the way into the position draft (both teams clubbed + protected).</summary>
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
        var joined = await join.Handle(new JoinDraftCommand(id, created.Summary.Version, guest), default);
        var locked = await @lock.Handle(new LockLobbyCommand(id, joined.Summary.Version, _host), default);
        var teams = await formTeams.Handle(new FormTeamsCommand(id, null, locked.Summary.Version, _host), default);
        var hostReady = await setReady.Handle(new SetReadyCommand(id, true, teams.Summary.Version, _host), default);
        var guestReady = await setReady.Handle(new SetReadyCommand(id, true, hostReady.Summary.Version, guest), default);
        var readyCheck = await beginReady.Handle(new BeginReadyCheckCommand(id, guestReady.Summary.Version, _host), default);
        var started = await start.Handle(new StartDraftCommand(id, readyCheck.Summary.Version, _host), default);
        var spun = await spinner.Handle(new CommitSpinnerCommand(id, started.Version, _host), default);
        var opened = await openClubs.Handle(new OpenClubSelectionCommand(id, spun.Summary.Version, _host), default);

        var version = opened.Summary.Version;
        foreach (var rank in new[] { 1, 2 })
        {
            var detail = await SnapshotAsync(id);
            var team = detail.Teams.First(candidate => candidate.SpinnerRank == rank);
            var club = _clubs[rank - 1];
            var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(ClubId: club.Id), default))
                .First(candidate => detail.Picks.All(pick => pick.FootballerId != candidate.Id));
            var result = await select.Handle(new SelectClubAndProtectCommand(id, club.Id, footballer.Id, version, team.MemberUserIds[0]), default);
            version = result.Summary.Version;
        }

        return await openPositions.Handle(new OpenPositionDraftCommand(id, version, _host), default);
    }

    private async Task<DraftDetail> SnapshotAsync(Guid draftId)
    {
        var draft = await _store.FindAsync(draftId, default);
        return DraftMapper.ToDetail(draft!);
    }

    /// <summary>Submits one valid pick for the active team/slot (mirrors what the board surfaces) and returns the snapshot.</summary>
    private async Task<DraftDetail> PickForActiveAsync(DraftDetail detail)
    {
        var turn = detail.Turn;
        var actor = turn.ActiveTeamMemberUserIds[0];
        var taken = detail.Picks.Select(pick => pick.FootballerId).ToHashSet();
        var position = turn.SlotAcceptsAnyPosition ? null : turn.ActiveSlotPosition;
        var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(Position: position), default))
            .First(candidate => !taken.Contains(candidate.Id));
        return await Pick().Handle(new SubmitPickCommand(detail.Summary.Id, footballer.Id, detail.Summary.Version, actor), default);
    }

    [Fact]
    public async Task A_full_position_draft_snakes_and_completes()
    {
        var detail = await PositionDraftAsync();
        var id = detail.Summary.Id;
        var teamRankById = detail.Teams.ToDictionary(team => team.Id, team => team.SpinnerRank!.Value);

        var pickRanks = new List<int>();
        while (detail.Summary.Status == "PositionDraft")
        {
            pickRanks.Add(teamRankById[detail.Turn.ActiveTeamId!.Value]);
            detail = await PickForActiveAsync(detail);
        }

        // 2 teams × 15 position slots, filled in snake order.
        var expected = Enumerable.Range(0, 30).Select(pick => DraftTurnOrder.NextPosition(2, pick).SpinnerRank).ToArray();
        Assert.Equal(expected, pickRanks);
        Assert.Equal("Completed", detail.Summary.Status);
        Assert.Contains(detail.Events, evt => evt.Type == nameof(DraftEventType.DraftCompleted));

        // Every team has a full 16-slot squad (1 held + 15 drafted) with no repeated footballer.
        foreach (var team in detail.Teams)
        {
            Assert.Equal(16, detail.Picks.Count(pick => pick.TeamId == team.Id));
        }
        Assert.Equal(detail.Picks.Count, detail.Picks.Select(pick => pick.FootballerId).Distinct().Count());
    }

    [Fact]
    public async Task Position_one_is_ST_in_ascending_order()
    {
        var detail = await PositionDraftAsync();
        Assert.Equal("PositionDraft", detail.Summary.Status);
        Assert.Equal(1, detail.Turn.Round);
        Assert.Equal("ST", detail.Turn.ActiveSlotPosition);
        Assert.Equal("Ascending", detail.Turn.Direction);
        Assert.Equal(1, detail.Teams.First(team => team.Id == detail.Turn.ActiveTeamId).SpinnerRank);
    }

    [Fact]
    public async Task An_out_of_turn_teammate_is_rejected()
    {
        var detail = await PositionDraftAsync();
        var inactiveTeam = detail.Teams.First(team => team.Id != detail.Turn.ActiveTeamId!.Value);
        var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(Position: "ST"), default))[0];

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Pick().Handle(new SubmitPickCommand(detail.Summary.Id, footballer.Id, detail.Summary.Version, inactiveTeam.MemberUserIds[0]), default));
    }

    [Fact]
    public async Task A_footballer_who_cannot_play_the_slot_is_rejected()
    {
        var detail = await PositionDraftAsync(); // slot 1 is ST
        var actor = detail.Turn.ActiveTeamMemberUserIds[0];
        var gkOnly = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(Position: "GK"), default))
            .First(candidate => !candidate.Positions.Contains("ST"));

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Pick().Handle(new SubmitPickCommand(detail.Summary.Id, gkOnly.Id, detail.Summary.Version, actor), default));
    }

    [Fact]
    public async Task A_footballer_can_only_be_taken_once()
    {
        var detail = await PositionDraftAsync();
        detail = await PickForActiveAsync(detail); // rank 1 takes an ST

        // The just-picked footballer is now unavailable to the next team for the same ST round.
        var taken = detail.Picks.Last(pick => pick.SlotOrder == 1);
        var actor = detail.Turn.ActiveTeamMemberUserIds[0];

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Pick().Handle(new SubmitPickCommand(detail.Summary.Id, taken.FootballerId, detail.Summary.Version, actor), default));
    }

    [Fact]
    public async Task A_pick_is_rejected_in_the_wrong_state()
    {
        // A freshly opened club round is not the position draft.
        var draft = Draft.Create("x", DraftFormat.OneVsOne, _host, FakeRosterTemplateService.TemplateId, "CODE");
        await _store.AddAsync(draft, default);
        var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(Position: "ST"), default))[0];

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Pick().Handle(new SubmitPickCommand(draft.Id, footballer.Id, draft.Version, _host), default));
    }
}
