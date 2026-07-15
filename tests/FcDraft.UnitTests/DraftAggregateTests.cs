using FcDraft.Domain.Entities;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Exercises the <see cref="Draft"/> aggregate directly: creation records the opening event, accepted
/// transitions bump the version and append immutable events, illegal transitions throw, configuration is
/// snapshotted once, and the state rebuilt from history matches the live aggregate (PR-10 done-when).
/// </summary>
public sealed class DraftAggregateTests
{
    private static readonly Guid Host = Guid.NewGuid();

    private static Draft NewDraft() =>
        Draft.Create("Friday Night", DraftFormat.OneVsOne, Host, FakeRosterTemplateService.TemplateId, "ABC123");

    [Fact]
    public void Create_starts_in_draft_state_with_the_opening_event()
    {
        var draft = NewDraft();

        Assert.Equal(DraftStatus.Draft, draft.Status);
        Assert.Equal(1, draft.Version);
        var opening = Assert.Single(draft.Events);
        Assert.Equal(DraftEventType.DraftCreated, opening.Type);
        Assert.Equal(1, opening.Sequence);
        Assert.Null(opening.FromStatus);
        Assert.Equal(DraftStatus.Draft, opening.ToStatus);
        Assert.Equal(1, opening.Version);
    }

    [Fact]
    public void Transition_bumps_the_version_and_appends_an_event()
    {
        var draft = NewDraft();

        var evt = draft.Transition(DraftStatus.Lobby, DraftEventType.ParticipantInvited, Host);

        Assert.Equal(DraftStatus.Lobby, draft.Status);
        Assert.Equal(2, draft.Version);
        Assert.Equal(2, draft.Events.Count);
        Assert.Equal(2, evt.Sequence);
        Assert.Equal(DraftStatus.Draft, evt.FromStatus);
        Assert.Equal(DraftStatus.Lobby, evt.ToStatus);
        Assert.Equal(2, evt.Version);
    }

    [Fact]
    public void Disallowed_transition_throws_and_leaves_the_draft_untouched()
    {
        var draft = NewDraft();

        Assert.Throws<InvalidDraftTransitionException>(() =>
            draft.Transition(DraftStatus.PositionDraft, DraftEventType.PickAccepted, Host));

        Assert.Equal(DraftStatus.Draft, draft.Status);
        Assert.Equal(1, draft.Version);
        Assert.Single(draft.Events);
    }

    [Fact]
    public void Start_and_completion_transitions_stamp_their_timestamps()
    {
        var draft = NewDraft();
        draft.Transition(DraftStatus.Lobby, DraftEventType.ParticipantInvited, Host);
        draft.Transition(DraftStatus.TeamFormation, DraftEventType.TeamsFormed, Host);
        draft.Transition(DraftStatus.ReadyCheck, DraftEventType.ParticipantReadied, Host);

        Assert.Null(draft.StartedAt);
        draft.Transition(DraftStatus.SpinnerRanking, DraftEventType.DraftStarted, Host);
        Assert.NotNull(draft.StartedAt);

        draft.Transition(DraftStatus.ClubSelection, DraftEventType.SpinnerOrderRevealed, Host);
        draft.Transition(DraftStatus.PositionDraft, DraftEventType.PositionRoundStarted, Host);
        Assert.Null(draft.CompletedAt);
        draft.Transition(DraftStatus.Completed, DraftEventType.DraftCompleted, Host);
        Assert.NotNull(draft.CompletedAt);
    }

    [Fact]
    public void Current_state_rebuilds_from_the_event_history()
    {
        var draft = NewDraft();
        draft.Transition(DraftStatus.Lobby, DraftEventType.ParticipantInvited, Host);
        draft.Transition(DraftStatus.TeamFormation, DraftEventType.TeamsFormed, Host);
        draft.Transition(DraftStatus.ReadyCheck, DraftEventType.ParticipantReadied, Host);

        var snapshot = DraftStateProjection.Replay(draft.Events);

        Assert.Equal(draft.Status, snapshot.Status);
        Assert.Equal(draft.Version, snapshot.Version);
        Assert.Equal(draft.Events.Count, snapshot.EventCount);
    }

    [Fact]
    public void Replay_rejects_a_history_that_does_not_begin_with_creation()
    {
        var draft = NewDraft();
        draft.Transition(DraftStatus.Lobby, DraftEventType.ParticipantInvited, Host);

        // Dropping the DraftCreated event breaks the invariant that history begins with creation.
        var withoutCreation = draft.Events.Where(evt => evt.Type != DraftEventType.DraftCreated).ToArray();

        Assert.Throws<InvalidOperationException>(() => DraftStateProjection.Replay(withoutCreation));
    }

    [Fact]
    public void Snapshot_configuration_copies_ordered_slots_and_pins_the_dataset_once()
    {
        var draft = NewDraft();
        var datasetVersion = Guid.NewGuid();
        var slots = new[]
        {
            new DraftRosterSlot { DraftId = draft.Id, Order = 1, SlotType = RosterSlotType.StartingPosition, Position = "ST", Label = "ST" },
            new DraftRosterSlot { DraftId = draft.Id, Order = 0, SlotType = RosterSlotType.Held, Label = "Held player" },
        };

        draft.SnapshotConfiguration(slots, pickTimerSeconds: 90, datasetVersion);

        Assert.Equal(90, draft.PickTimerSeconds);
        Assert.Equal(datasetVersion, draft.PinnedDatasetVersionId);
        Assert.Equal([0, 1], draft.Slots.OrderBy(slot => slot.Order).Select(slot => slot.Order));

        // A second snapshot must be refused so a retried start cannot double-write.
        Assert.Throws<InvalidOperationException>(() => draft.SnapshotConfiguration(slots, 120, datasetVersion));
    }
}
