using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Drafts;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the PR-18 room reads: the board's search/take narrow and deliberately widen the eligible pool
/// without ever leaving the pinned catalog, and the footballer detail-card query surfaces the §9.6 card
/// (league, nation, stats/roles/PlayStyles JSON) plus this draft's availability — including who holds a
/// taken footballer — while keeping the 404-not-403 visibility rule of every other draft read.
/// </summary>
public sealed class DraftBoardAndCardQueryTests
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

    public DraftBoardAndCardQueryTests()
    {
        _host = _identity.Add("Host").Id;
        _clubs = _catalog.SeedStandardLeague();
    }

    private DraftExpiryService Expiry() => new(_store, _catalog, _identity, _runner, new NullDraftNotifier(), _clock, TestNotifiers.Lifecycle(_identity));

    private GetDraftBoardQueryHandler Board() => new(_store, _catalog, Expiry(), _clock);

    private GetDraftFootballerQueryHandler Card() => new(_store, _catalog);

    /// <summary>Drives a 1v1 draft into the position draft (both teams clubbed + protected), slot 1 = ST.</summary>
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
        var detail = opened;
        foreach (var rank in new[] { 1, 2 })
        {
            var team = detail.Teams.First(candidate => candidate.SpinnerRank == rank);
            var club = _clubs[rank - 1];
            var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(ClubId: club.Id), default))
                .First(candidate => detail.Picks.All(pick => pick.FootballerId != candidate.Id));
            detail = await select.Handle(new SelectClubAndProtectCommand(id, club.Id, footballer.Id, version, team.MemberUserIds[0]), default);
            version = detail.Summary.Version;
        }

        return await openPositions.Handle(new OpenPositionDraftCommand(id, version, _host), default);
    }

    [Fact]
    public async Task Board_search_narrows_the_eligible_pool_by_name()
    {
        var detail = await PositionDraftAsync(); // slot 1 = ST
        var id = detail.Summary.Id;

        var unfiltered = await Board().Handle(new GetDraftBoardQuery(id, _host, false), default);
        Assert.NotNull(unfiltered);
        Assert.True(unfiltered!.EligibleFootballers.Count > 1);

        // Every seeded ST is named "<club> ST<n>"; search for one club's STs only.
        var term = $"{_clubs[0].Name} ST";
        var searched = await Board().Handle(new GetDraftBoardQuery(id, _host, false, Search: term), default);
        Assert.NotNull(searched);
        Assert.NotEmpty(searched!.EligibleFootballers);
        Assert.All(searched.EligibleFootballers, footballer =>
            Assert.Contains(term, footballer.Name, StringComparison.OrdinalIgnoreCase));
        Assert.True(searched.EligibleFootballers.Count < unfiltered.EligibleFootballers.Count);

        // The searched pool is still position-scoped and availability-filtered (held players are gone).
        var held = detail.Picks.Where(pick => pick.SlotOrder == 0).Select(pick => pick.FootballerId).ToHashSet();
        Assert.All(searched.EligibleFootballers, footballer => Assert.DoesNotContain(footballer.Id, held));
    }

    [Fact]
    public async Task Board_take_bounds_the_returned_pool_deliberately()
    {
        var detail = await PositionDraftAsync();
        var bounded = await Board().Handle(new GetDraftBoardQuery(detail.Summary.Id, _host, false, Take: 3), default);
        Assert.NotNull(bounded);
        Assert.Equal(3, bounded!.EligibleFootballers.Count);
    }

    [Fact]
    public async Task The_card_surfaces_the_pinned_facts_for_an_available_footballer()
    {
        var detail = await PositionDraftAsync();
        var board = await Board().Handle(new GetDraftBoardQuery(detail.Summary.Id, _host, false), default);
        var target = board!.EligibleFootballers[0];
        _catalog.SetCardExtras(
            target.Id, nation: "France",
            statsJson: """[{"label":"PAC","value":97}]""",
            rolesJson: """[{"position":"ST","name":"Advanced Forward","familiarity":2}]""",
            playStylesJson: """[{"name":"Rapid","plus":true}]""");

        var card = await Card().Handle(new GetDraftFootballerQuery(detail.Summary.Id, target.Id, _host, false), default);

        Assert.NotNull(card);
        Assert.False(card!.IsTaken);
        Assert.Null(card.TakenByTeamName);
        Assert.Equal(target.Name, card.Card.Name);
        Assert.Equal("Test League", card.Card.League);
        Assert.Equal("France", card.Card.Nation);
        Assert.Equal(97, card.Card.Stats[0].GetProperty("value").GetInt32());
        Assert.Equal(2, card.Card.Roles[0].GetProperty("familiarity").GetInt32());
        Assert.True(card.Card.PlayStyles[0].GetProperty("plus").GetBoolean());
    }

    [Fact]
    public async Task The_card_explains_who_holds_a_taken_footballer()
    {
        var detail = await PositionDraftAsync();
        var held = detail.Picks.First(pick => pick.SlotOrder == 0); // protected in the club round

        var card = await Card().Handle(new GetDraftFootballerQuery(detail.Summary.Id, held.FootballerId, _host, false), default);

        Assert.NotNull(card);
        Assert.True(card!.IsTaken);
        Assert.Equal(held.TeamId, card.TakenByTeamId);
        Assert.Equal(detail.Teams.First(team => team.Id == held.TeamId).Name, card.TakenByTeamName);
        Assert.Equal("Held player", card.TakenSlotLabel);
    }

    [Fact]
    public async Task The_card_is_hidden_from_non_participants_like_every_draft_read()
    {
        var detail = await PositionDraftAsync();
        var outsider = _identity.Add("Outsider").Id;
        var target = detail.Picks[0].FootballerId;

        Assert.Null(await Card().Handle(new GetDraftFootballerQuery(detail.Summary.Id, target, outsider, false), default));
        // An unknown footballer id is also null (404), even for a participant.
        Assert.Null(await Card().Handle(new GetDraftFootballerQuery(detail.Summary.Id, 999_999, _host, false), default));
    }
}
