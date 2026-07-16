using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Features.Datasets;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Drafts;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the PR-14 club/protected-player round over the in-memory store: the host opens the round, each team
/// (in straight spinner order) picks a unique five-star club and protects one 75+ player from it, and the
/// position draft cannot open until every team is set. Uniqueness, club-match, out-of-turn, and eligibility
/// rejections are all covered.
/// </summary>
public sealed class ClubSelectionCommandHandlerTests
{
    private readonly InMemoryDraftStore _store = new();
    private readonly InMemoryTransactionRunner _runner = new();
    private readonly FakeRosterTemplateService _templates = new();
    private readonly FakeDatasetAdminService _datasets = new();
    private readonly FakeIdentityDirectory _identity = new();
    private readonly ReversingShuffler _shuffler = new();
    private readonly FakeDraftCatalog _catalog = new();
    private readonly IReadOnlyList<CatalogClub> _clubs;
    private readonly Guid _host;

    public ClubSelectionCommandHandlerTests()
    {
        _host = _identity.Add("Host").Id;
        _clubs = _catalog.SeedStandardLeague();
    }

    private readonly TestClock _clock = new(new DateTimeOffset(2026, 07, 16, 12, 00, 00, TimeSpan.Zero));

    private OpenClubSelectionCommandHandler OpenClubs() => new(_store, _identity, _catalog, _runner);
    private SelectClubAndProtectCommandHandler Select() => new(_store, _identity, _catalog, _runner);
    private OpenPositionDraftCommandHandler OpenPositions() => new(_store, _identity, _catalog, _runner, _clock);

    /// <summary>Drives a 1v1 draft to the committed-spinner state and opens club selection; returns the snapshot.</summary>
    private async Task<DraftDetail> ClubRoundAsync()
    {
        var guest = _identity.Add("Guest").Id;
        var create = new CreateDraftCommandHandler(_store, _templates, _identity, _runner);
        var join = new JoinDraftCommandHandler(_store, _identity, _runner);
        var @lock = new LockLobbyCommandHandler(_store, _identity, _runner);
        var formTeams = new FormTeamsCommandHandler(_store, _identity, _runner);
        var setReady = new SetReadyCommandHandler(_store, _identity, _runner);
        var beginReady = new BeginReadyCheckCommandHandler(_store, _identity, _runner);
        var start = new StartDraftCommandHandler(_store, _templates, _datasets, _runner);
        var spinner = new CommitSpinnerCommandHandler(_store, _identity, _shuffler, _runner);

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
        return await OpenClubs().Handle(new OpenClubSelectionCommand(id, spun.Summary.Version, _host), default);
    }

    private async Task<int> ProtectForRankAsync(Guid draftId, int version, int rank, CatalogClub club)
    {
        var detail = await GetAsync(draftId);
        var team = detail.Teams.First(candidate => candidate.SpinnerRank == rank);
        var actor = team.MemberUserIds[0];
        var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(ClubId: club.Id), default))[0];
        var result = await Select().Handle(new SelectClubAndProtectCommand(draftId, club.Id, footballer.Id, version, actor), default);
        return result.Summary.Version;
    }

    private async Task<DraftDetail> GetAsync(Guid draftId)
    {
        var draft = await _store.FindAsync(draftId, default);
        return DraftMapper.ToDetail(draft!);
    }

    [Fact]
    public async Task Opening_club_selection_is_host_only_and_moves_to_club_selection()
    {
        var opened = await ClubRoundAsync();
        Assert.Equal("ClubSelection", opened.Summary.Status);
        Assert.Contains(opened.Events, evt => evt.Type == nameof(DraftEventType.ClubSelectionStarted));
    }

    [Fact]
    public async Task Each_team_in_straight_order_picks_a_unique_club_and_protects_a_player()
    {
        var opened = await ClubRoundAsync();
        var id = opened.Summary.Id;

        // The active team is rank 1 first (straight order).
        Assert.Equal(1, opened.Teams.First(team => team.Id == opened.Turn.ActiveTeamId).SpinnerRank);

        var version = await ProtectForRankAsync(id, opened.Summary.Version, 1, _clubs[0]);
        version = await ProtectForRankAsync(id, version, 2, _clubs[1]);

        var final = await GetAsync(id);
        Assert.All(final.Teams, team => Assert.NotNull(team.SelectedClubId));
        Assert.Equal(2, final.Picks.Count(pick => pick.SlotOrder == Draft.HeldSlotOrder));
        // Distinct clubs.
        Assert.Equal(2, final.Teams.Select(team => team.SelectedClubId).Distinct().Count());
    }

    [Fact]
    public async Task A_club_cannot_be_chosen_by_two_teams()
    {
        var opened = await ClubRoundAsync();
        var id = opened.Summary.Id;
        var version = await ProtectForRankAsync(id, opened.Summary.Version, 1, _clubs[0]);

        // Rank 2 tries to take the same club rank 1 already chose.
        var detail = await GetAsync(id);
        var rank2 = detail.Teams.First(team => team.SpinnerRank == 2);
        var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(ClubId: _clubs[0].Id), default))
            .First(candidate => detail.Picks.All(pick => pick.FootballerId != candidate.Id));

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Select().Handle(new SelectClubAndProtectCommand(id, _clubs[0].Id, footballer.Id, version, rank2.MemberUserIds[0]), default));
    }

    [Fact]
    public async Task A_protected_player_must_play_for_the_chosen_club()
    {
        var opened = await ClubRoundAsync();
        var id = opened.Summary.Id;
        var detail = await GetAsync(id);
        var rank1 = detail.Teams.First(team => team.SpinnerRank == 1);

        // A player from a different club cannot be protected under _clubs[0].
        var wrongClubPlayer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(ClubId: _clubs[1].Id), default))[0];

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Select().Handle(new SelectClubAndProtectCommand(id, _clubs[0].Id, wrongClubPlayer.Id, opened.Summary.Version, rank1.MemberUserIds[0]), default));
    }

    [Fact]
    public async Task A_team_cannot_pick_out_of_turn()
    {
        var opened = await ClubRoundAsync();
        var id = opened.Summary.Id;
        var detail = await GetAsync(id);
        var rank2 = detail.Teams.First(team => team.SpinnerRank == 2);
        var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(ClubId: _clubs[1].Id), default))[0];

        // Rank 2 tries to act while it is rank 1's turn.
        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Select().Handle(new SelectClubAndProtectCommand(id, _clubs[1].Id, footballer.Id, opened.Summary.Version, rank2.MemberUserIds[0]), default));
    }

    [Fact]
    public async Task An_ineligible_footballer_is_rejected()
    {
        var opened = await ClubRoundAsync();
        var id = opened.Summary.Id;
        var detail = await GetAsync(id);
        var rank1 = detail.Teams.First(team => team.SpinnerRank == 1);

        // Footballer id 999999 is not in the catalog (not 75+/Kick Off).
        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Select().Handle(new SelectClubAndProtectCommand(id, _clubs[0].Id, 999999, opened.Summary.Version, rank1.MemberUserIds[0]), default));
    }

    [Fact]
    public async Task The_position_draft_cannot_open_until_every_team_is_set()
    {
        var opened = await ClubRoundAsync();
        var id = opened.Summary.Id;
        var version = await ProtectForRankAsync(id, opened.Summary.Version, 1, _clubs[0]);

        // Only one team has chosen — opening the position draft is rejected.
        await Assert.ThrowsAsync<ValidationAppException>(() =>
            OpenPositions().Handle(new OpenPositionDraftCommand(id, version, _host), default));

        version = await ProtectForRankAsync(id, version, 2, _clubs[1]);
        var opened2 = await OpenPositions().Handle(new OpenPositionDraftCommand(id, version, _host), default);
        Assert.Equal("PositionDraft", opened2.Summary.Status);
        Assert.Contains(opened2.Events, evt => evt.Type == nameof(DraftEventType.PositionRoundStarted));
    }

    [Fact]
    public async Task A_stale_version_conflicts()
    {
        var opened = await ClubRoundAsync();
        var id = opened.Summary.Id;
        var detail = await GetAsync(id);
        var rank1 = detail.Teams.First(team => team.SpinnerRank == 1);
        var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(ClubId: _clubs[0].Id), default))[0];

        // A version one behind the current snapshot conflicts.
        await Assert.ThrowsAsync<ConflictAppException>(() =>
            Select().Handle(new SelectClubAndProtectCommand(id, _clubs[0].Id, footballer.Id, opened.Summary.Version - 1, rank1.MemberUserIds[0]), default));
    }
}
