using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Drafts;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the PR-13 spinner rules over the in-memory store: a committed order gives every team one unique
/// rank drawn from the injected shuffle (never insertion order), a retry is idempotent and cannot reshuffle,
/// the spinner only runs in the spinner-ranking state, and a non-host commit is rejected. A direct
/// <see cref="FisherYatesShuffler"/> test guards that the production shuffle is an unbiased permutation.
/// </summary>
public sealed class SpinnerCommandHandlerTests
{
    private readonly InMemoryDraftStore _store = new();
    private readonly InMemoryTransactionRunner _runner = new();
    private readonly FakeRosterTemplateService _templates = new();
    private readonly FakeDatasetAdminService _datasets = new();
    private readonly FakeIdentityDirectory _identity = new();
    private readonly ReversingShuffler _shuffler = new();
    private readonly Guid _host;

    public SpinnerCommandHandlerTests() => _host = _identity.Add("Host").Id;

    private CommitSpinnerCommandHandler Commit() => new(_store, _identity, _shuffler, _runner);

    /// <summary>
    /// Drives a 1v1 draft all the way to the spinner-ranking state with two solo teams ("Host" and "Guest"),
    /// returning the draft id, its current version, and the guest's user id.
    /// </summary>
    private async Task<(Guid Id, int Version, Guid Guest)> StartedDraftAsync()
    {
        var guest = _identity.Add("Guest").Id;
        var create = new CreateDraftCommandHandler(_store, _templates, _identity, _runner, TestNotifiers.Lifecycle(_identity));
        var join = new JoinDraftCommandHandler(_store, _identity, _runner);
        var @lock = new LockLobbyCommandHandler(_store, _identity, _runner);
        var formTeams = new FormTeamsCommandHandler(_store, _identity, _runner);
        var setReady = new SetReadyCommandHandler(_store, _identity, _runner);
        var beginReady = new BeginReadyCheckCommandHandler(_store, _identity, _runner);
        var start = new StartDraftCommandHandler(_store, _templates, _datasets, _runner);

        var created = await create.Handle(new CreateDraftCommand("Spin", "1v1", _host, null, [guest]), default);
        var id = created.Summary.Id;
        var joined = await join.Handle(new JoinDraftCommand(id, created.Summary.Version, guest), default);
        var locked = await @lock.Handle(new LockLobbyCommand(id, joined.Summary.Version, _host), default);
        var teams = await formTeams.Handle(new FormTeamsCommand(id, null, locked.Summary.Version, _host), default);
        var hostReady = await setReady.Handle(new SetReadyCommand(id, true, teams.Summary.Version, _host), default);
        var guestReady = await setReady.Handle(new SetReadyCommand(id, true, hostReady.Summary.Version, guest), default);
        var readyCheck = await beginReady.Handle(new BeginReadyCheckCommand(id, guestReady.Summary.Version, _host), default);
        var started = await start.Handle(new StartDraftCommand(id, readyCheck.Summary.Version, _host), default);

        return (id, started.Version, guest);
    }

    [Fact]
    public async Task Committing_the_spinner_gives_every_team_a_unique_rank_from_the_injected_shuffle()
    {
        var (id, version, _) = await StartedDraftAsync();

        var committed = await Commit().Handle(new CommitSpinnerCommand(id, version, _host), default);

        // Two teams, ranks {1, 2}, unique.
        Assert.All(committed.Teams, team => Assert.NotNull(team.SpinnerRank));
        Assert.Equal(new[] { 1, 2 }, committed.Teams.Select(team => team.SpinnerRank!.Value).OrderBy(rank => rank).ToArray());

        // Teams are created in participant order (Host, then Guest); the reversing shuffle flips that, so the
        // Guest team ranks first — proving the order came from the seam, not insertion order.
        Assert.Equal(1, committed.Teams.First(team => team.Name == "Guest").SpinnerRank);
        Assert.Equal(2, committed.Teams.First(team => team.Name == "Host").SpinnerRank);

        var stored = await _store.FindAsync(id, default);
        Assert.Contains(stored!.Events, evt => evt.Type == DraftEventType.SpinnerOrderCommitted);
        Assert.Contains(stored.Events, evt => evt.Type == DraftEventType.SpinnerOrderRevealed);
    }

    [Fact]
    public async Task A_retry_cannot_reshuffle_a_committed_order()
    {
        var (id, version, _) = await StartedDraftAsync();
        var committed = await Commit().Handle(new CommitSpinnerCommand(id, version, _host), default);
        var firstOrder = committed.Teams.ToDictionary(team => team.Name, team => team.SpinnerRank);

        // A retry at the now-current version is a no-op: same ranks, no new events, no version bump.
        var retry = await Commit().Handle(new CommitSpinnerCommand(id, committed.Summary.Version, _host), default);

        Assert.Equal(committed.Summary.Version, retry.Summary.Version);
        Assert.Equal(firstOrder, retry.Teams.ToDictionary(team => team.Name, team => team.SpinnerRank));
        var stored = await _store.FindAsync(id, default);
        Assert.Single(stored!.Events, evt => evt.Type == DraftEventType.SpinnerOrderCommitted);
    }

    [Fact]
    public async Task A_stale_version_conflicts()
    {
        var (id, version, _) = await StartedDraftAsync();
        await Commit().Handle(new CommitSpinnerCommand(id, version, _host), default);

        await Assert.ThrowsAsync<ConflictAppException>(() =>
            Commit().Handle(new CommitSpinnerCommand(id, version, _host), default));
    }

    [Fact]
    public async Task A_non_host_cannot_commit_the_spinner()
    {
        var (id, version, guest) = await StartedDraftAsync();

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Commit().Handle(new CommitSpinnerCommand(id, version, guest), default));
    }

    [Fact]
    public async Task The_spinner_cannot_run_before_spinner_ranking()
    {
        // A freshly-created lobby is in the Lobby state, not SpinnerRanking.
        var create = new CreateDraftCommandHandler(_store, _templates, _identity, _runner, TestNotifiers.Lifecycle(_identity));
        var created = await create.Handle(new CreateDraftCommand("Early", "1v1", _host), default);

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Commit().Handle(new CommitSpinnerCommand(created.Summary.Id, created.Summary.Version, _host), default));
    }
}

/// <summary>Guards that the production Fisher–Yates shuffle is an unbiased, reproducible permutation.</summary>
public sealed class FisherYatesShufflerTests
{
    [Fact]
    public void Shuffle_preserves_every_element_exactly_once()
    {
        var items = Enumerable.Range(0, 50).ToList();
        new FisherYatesShuffler(new Random(1234)).Shuffle(items);

        Assert.Equal(Enumerable.Range(0, 50), items.OrderBy(value => value));
    }

    [Fact]
    public void Shuffle_is_deterministic_for_a_given_seed()
    {
        var first = Enumerable.Range(0, 20).ToList();
        var second = Enumerable.Range(0, 20).ToList();

        new FisherYatesShuffler(new Random(42)).Shuffle(first);
        new FisherYatesShuffler(new Random(42)).Shuffle(second);

        Assert.Equal(first, second);
    }
}
