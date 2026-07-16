using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Drafts;
using FcDraft.Infrastructure.Rosters;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Drives the draft creation, transition, and start handlers over the in-memory store + pass-through
/// transaction runner. Creating a lobby now opens it (Draft → Lobby) with the host as a joined participant
/// (PR-11); the transition/start behaviours from PR-10 are otherwise unchanged: allowed transitions advance
/// and append, a stale version conflicts, an illegal transition is rejected with no partial write, only the
/// host/admin may control it, and start snapshots the bound template.
/// </summary>
public sealed class DraftCommandHandlerTests
{
    private readonly InMemoryDraftStore _store = new();
    private readonly InMemoryTransactionRunner _runner = new();
    private readonly FakeRosterTemplateService _templates = new();
    private readonly FakeDatasetAdminService _datasets = new();
    private readonly FakeIdentityDirectory _identity = new();
    private readonly Guid _host;

    public DraftCommandHandlerTests() => _host = _identity.Add("Host").Id;

    private CreateDraftCommandHandler Create() => new(_store, _templates, _identity, _runner);
    private TransitionDraftCommandHandler Transition() => new(_store, _runner);
    private StartDraftCommandHandler Start() => new(_store, _templates, _datasets, _runner);
    private JoinDraftCommandHandler Join() => new(_store, _identity, _runner);
    private LockLobbyCommandHandler Lock() => new(_store, _identity, _runner);
    private FormTeamsCommandHandler FormTeams() => new(_store, _identity, _runner);
    private SetReadyCommandHandler SetReady() => new(_store, _identity, _runner);
    private BeginReadyCheckCommandHandler BeginReadyCheck() => new(_store, _identity, _runner);

    private async Task<DraftSummary> CreateDraftAsync() =>
        (await Create().Handle(new CreateDraftCommand("Friday Night", "1v1", _host), default)).Summary;

    /// <summary>
    /// Builds a fully start-ready 1v1 draft (host + one joined guest, solo teams formed, both ready, in the
    /// ready check) so the Start gate (§9.4) is satisfied. Returns the draft id and its current version.
    /// </summary>
    private async Task<(Guid Id, int Version)> ReadyToStartDraftAsync()
    {
        var guest = _identity.Add("Guest").Id;
        var created = await Create().Handle(new CreateDraftCommand("Friday Night", "1v1", _host, null, [guest]), default);
        var id = created.Summary.Id;

        var joined = await Join().Handle(new JoinDraftCommand(id, created.Summary.Version, guest), default);
        var locked = await Lock().Handle(new LockLobbyCommand(id, joined.Summary.Version, _host), default);
        var teams = await FormTeams().Handle(new FormTeamsCommand(id, null, locked.Summary.Version, _host), default);
        var hostReady = await SetReady().Handle(new SetReadyCommand(id, true, teams.Summary.Version, _host), default);
        var guestReady = await SetReady().Handle(new SetReadyCommand(id, true, hostReady.Summary.Version, guest), default);
        var ready = await BeginReadyCheck().Handle(new BeginReadyCheckCommand(id, guestReady.Summary.Version, _host), default);
        return (id, ready.Summary.Version);
    }

    [Fact]
    public async Task Create_opens_a_lobby_with_the_host_joined_and_the_opening_events()
    {
        var summary = await CreateDraftAsync();

        // DraftCreated (v1, Draft) then the host joining opens the lobby (v2, Lobby).
        Assert.Equal("Lobby", summary.Status);
        Assert.Equal(2, summary.Version);
        Assert.Equal(1, summary.ParticipantCount);
        Assert.False(string.IsNullOrWhiteSpace(summary.Code));

        var stored = await _store.FindAsync(summary.Id, default);
        Assert.NotNull(stored);
        Assert.Equal(DraftEventType.DraftCreated, stored!.Events.OrderBy(e => e.Sequence).First().Type);
        var host = Assert.Single(stored.Participants);
        Assert.True(host.IsHost);
        Assert.Equal(DraftParticipantStatus.Joined, host.Status);
        Assert.Equal(_host, host.UserId);
    }

    [Fact]
    public async Task Create_without_an_active_template_is_a_validation_error()
    {
        var handler = new CreateDraftCommandHandler(
            _store, new FakeRosterTemplateService(hasActive: false), _identity, _runner);

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            handler.Handle(new CreateDraftCommand("No template", "1v1", _host), default));
    }

    [Fact]
    public async Task Allowed_transition_advances_the_state_and_appends_an_event()
    {
        var draft = await CreateDraftAsync();

        var moved = await Transition().Handle(
            new TransitionDraftCommand(draft.Id, "TeamFormation", "TeamsFormed", ExpectedVersion: 2, _host), default);

        Assert.Equal("TeamFormation", moved.Status);
        Assert.Equal(3, moved.Version);

        var stored = await _store.FindAsync(draft.Id, default);
        Assert.Equal(3, stored!.Events.Count);
    }

    [Fact]
    public async Task A_stale_expected_version_is_a_conflict()
    {
        var draft = await CreateDraftAsync();
        await Transition().Handle(new TransitionDraftCommand(draft.Id, "TeamFormation", "TeamsFormed", 2, _host), default);

        // The draft is now version 3; sending the last-seen version 2 must lose.
        await Assert.ThrowsAsync<ConflictAppException>(() =>
            Transition().Handle(new TransitionDraftCommand(draft.Id, "ReadyCheck", "ParticipantReadied", 2, _host), default));
    }

    [Fact]
    public async Task An_illegal_transition_is_rejected_with_no_partial_write()
    {
        var draft = await CreateDraftAsync();

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Transition().Handle(new TransitionDraftCommand(draft.Id, "PositionDraft", "PickAccepted", 2, _host), default));

        var stored = await _store.FindAsync(draft.Id, default);
        Assert.Equal(DraftStatus.Lobby, stored!.Status);
        Assert.Equal(2, stored.Version);
        Assert.Equal(2, stored.Events.Count);
    }

    [Fact]
    public async Task An_unknown_draft_is_not_found()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Transition().Handle(new TransitionDraftCommand(Guid.NewGuid(), "Lobby", "ParticipantInvited", 1, _host), default));
    }

    [Fact]
    public async Task Only_the_host_or_an_admin_may_control_the_draft()
    {
        var draft = await CreateDraftAsync();
        var stranger = Guid.NewGuid();

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Transition().Handle(new TransitionDraftCommand(draft.Id, "TeamFormation", "TeamsFormed", 2, stranger), default));

        // An admin (even if not the host) is allowed.
        var moved = await Transition().Handle(
            new TransitionDraftCommand(draft.Id, "TeamFormation", "TeamsFormed", 2, stranger, ActorIsAdmin: true), default);
        Assert.Equal("TeamFormation", moved.Status);
    }

    [Fact]
    public async Task Transitioning_to_spinner_ranking_directly_is_rejected_in_favour_of_start()
    {
        var draft = await CreateDraftAsync();

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Transition().Handle(new TransitionDraftCommand(draft.Id, "SpinnerRanking", "DraftStarted", 2, _host), default));
    }

    [Fact]
    public async Task Start_snapshots_configuration_and_advances_to_spinner_ranking()
    {
        var (id, version) = await ReadyToStartDraftAsync();

        var started = await Start().Handle(new StartDraftCommand(id, version, _host), default);

        Assert.Equal("SpinnerRanking", started.Status);
        Assert.Equal(FakeDatasetAdminService.ActiveVersionId, started.PinnedDatasetVersionId);
        Assert.Equal(DefaultRosterTemplate.PickTimerSeconds, started.PickTimerSeconds);

        var stored = await _store.FindAsync(id, default);
        Assert.Equal(DefaultRosterTemplate.Slots().Count, stored!.Slots.Count);
        Assert.Contains(stored.Events, evt => evt.Type == DraftEventType.DraftStarted);
    }

    [Fact]
    public async Task Start_from_the_wrong_state_is_rejected()
    {
        var draft = await CreateDraftAsync();

        // The freshly-created lobby is in Lobby, not ReadyCheck, so it cannot start yet.
        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Start().Handle(new StartDraftCommand(draft.Id, 2, _host), default));
    }

    [Fact]
    public async Task Start_is_rejected_when_a_participant_is_not_ready()
    {
        // Build the ready-to-start draft, then flip the guest back to not-ready in the ready check.
        var (id, version) = await ReadyToStartDraftAsync();
        var stored = await _store.FindAsync(id, default);
        var guestId = stored!.Participants.First(participant => !participant.IsHost).UserId;
        var unready = await SetReady().Handle(new SetReadyCommand(id, false, version, guestId), default);

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Start().Handle(new StartDraftCommand(id, unready.Summary.Version, _host), default));
    }
}
