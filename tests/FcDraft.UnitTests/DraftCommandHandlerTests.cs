using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Drafts;
using FcDraft.Infrastructure.Rosters;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Drives the draft command handlers over the in-memory store + pass-through transaction runner, proving
/// the PR-10 behaviours: create records the opening event, allowed transitions advance and append, a stale
/// version conflicts, an illegal transition is rejected with no partial write, an unknown draft is a
/// not-found, only the host/admin may control it, and start snapshots configuration.
/// </summary>
public sealed class DraftCommandHandlerTests
{
    private readonly InMemoryDraftStore _store = new();
    private readonly InMemoryTransactionRunner _runner = new();
    private readonly FakeRosterTemplateService _templates = new();
    private readonly FakeDatasetAdminService _datasets = new();
    private readonly Guid _host = Guid.NewGuid();

    private CreateDraftCommandHandler Create() => new(_store, _templates, _runner);
    private TransitionDraftCommandHandler Transition() => new(_store, _runner);
    private StartDraftCommandHandler Start() => new(_store, _templates, _datasets, _runner);

    private Task<DraftSummary> CreateDraftAsync() =>
        Create().Handle(new CreateDraftCommand("Friday Night", "1v1", _host), default);

    [Fact]
    public async Task Create_persists_a_draft_in_the_draft_state_with_the_opening_event()
    {
        var summary = await CreateDraftAsync();

        Assert.Equal("Draft", summary.Status);
        Assert.Equal(1, summary.Version);
        Assert.False(string.IsNullOrWhiteSpace(summary.Code));

        var stored = await _store.FindAsync(summary.Id, default);
        Assert.NotNull(stored);
        var opening = Assert.Single(stored!.Events);
        Assert.Equal(DraftEventType.DraftCreated, opening.Type);
    }

    [Fact]
    public async Task Create_without_an_active_template_is_a_validation_error()
    {
        var handler = new CreateDraftCommandHandler(_store, new FakeRosterTemplateService(hasActive: false), _runner);

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            handler.Handle(new CreateDraftCommand("No template", "1v1", _host), default));
    }

    [Fact]
    public async Task Allowed_transition_advances_the_state_and_appends_an_event()
    {
        var draft = await CreateDraftAsync();

        var moved = await Transition().Handle(
            new TransitionDraftCommand(draft.Id, "Lobby", "ParticipantInvited", ExpectedVersion: 1, _host), default);

        Assert.Equal("Lobby", moved.Status);
        Assert.Equal(2, moved.Version);

        var stored = await _store.FindAsync(draft.Id, default);
        Assert.Equal(2, stored!.Events.Count);
    }

    [Fact]
    public async Task A_stale_expected_version_is_a_conflict()
    {
        var draft = await CreateDraftAsync();
        await Transition().Handle(new TransitionDraftCommand(draft.Id, "Lobby", "ParticipantInvited", 1, _host), default);

        // The draft is now version 2; sending the last-seen version 1 must lose.
        await Assert.ThrowsAsync<ConflictAppException>(() =>
            Transition().Handle(new TransitionDraftCommand(draft.Id, "TeamFormation", "TeamsFormed", 1, _host), default));
    }

    [Fact]
    public async Task An_illegal_transition_is_rejected_with_no_partial_write()
    {
        var draft = await CreateDraftAsync();

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Transition().Handle(new TransitionDraftCommand(draft.Id, "PositionDraft", "PickAccepted", 1, _host), default));

        var stored = await _store.FindAsync(draft.Id, default);
        Assert.Equal(DraftStatus.Draft, stored!.Status);
        Assert.Equal(1, stored.Version);
        Assert.Single(stored.Events);
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
            Transition().Handle(new TransitionDraftCommand(draft.Id, "Lobby", "ParticipantInvited", 1, stranger), default));

        // An admin (even if not the host) is allowed.
        var moved = await Transition().Handle(
            new TransitionDraftCommand(draft.Id, "Lobby", "ParticipantInvited", 1, stranger, ActorIsAdmin: true), default);
        Assert.Equal("Lobby", moved.Status);
    }

    [Fact]
    public async Task Transitioning_to_spinner_ranking_directly_is_rejected_in_favour_of_start()
    {
        var draft = await CreateDraftAsync();

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Transition().Handle(new TransitionDraftCommand(draft.Id, "SpinnerRanking", "DraftStarted", 1, _host), default));
    }

    [Fact]
    public async Task Start_snapshots_configuration_and_advances_to_spinner_ranking()
    {
        var draft = await CreateDraftAsync();
        await Transition().Handle(new TransitionDraftCommand(draft.Id, "Lobby", "ParticipantInvited", 1, _host), default);
        await Transition().Handle(new TransitionDraftCommand(draft.Id, "TeamFormation", "TeamsFormed", 2, _host), default);
        await Transition().Handle(new TransitionDraftCommand(draft.Id, "ReadyCheck", "ParticipantReadied", 3, _host), default);

        var started = await Start().Handle(new StartDraftCommand(draft.Id, ExpectedVersion: 4, _host), default);

        Assert.Equal("SpinnerRanking", started.Status);
        Assert.Equal(5, started.Version);
        Assert.Equal(FakeDatasetAdminService.ActiveVersionId, started.PinnedDatasetVersionId);
        Assert.Equal(DefaultRosterTemplate.PickTimerSeconds, started.PickTimerSeconds);

        var stored = await _store.FindAsync(draft.Id, default);
        Assert.Equal(DefaultRosterTemplate.Slots().Count, stored!.Slots.Count);
        Assert.Contains(stored.Events, evt => evt.Type == DraftEventType.DraftStarted);
    }

    [Fact]
    public async Task Start_from_the_wrong_state_is_rejected()
    {
        var draft = await CreateDraftAsync();

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Start().Handle(new StartDraftCommand(draft.Id, 1, _host), default));
    }
}
