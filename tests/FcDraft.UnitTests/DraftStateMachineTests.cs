using FcDraft.Domain.Entities;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>Proves the allowed transition table matches PRD §10.1 exactly (the PR-10 done-when).</summary>
public sealed class DraftStateMachineTests
{
    [Theory]
    // Draft
    [InlineData(DraftStatus.Draft, DraftStatus.Lobby, true)]
    [InlineData(DraftStatus.Draft, DraftStatus.Cancelled, true)]
    // Lobby
    [InlineData(DraftStatus.Lobby, DraftStatus.TeamFormation, true)]
    [InlineData(DraftStatus.Lobby, DraftStatus.Cancelled, true)]
    // TeamFormation
    [InlineData(DraftStatus.TeamFormation, DraftStatus.ReadyCheck, true)]
    [InlineData(DraftStatus.TeamFormation, DraftStatus.Lobby, true)]
    [InlineData(DraftStatus.TeamFormation, DraftStatus.Cancelled, true)]
    // ReadyCheck
    [InlineData(DraftStatus.ReadyCheck, DraftStatus.SpinnerRanking, true)]
    [InlineData(DraftStatus.ReadyCheck, DraftStatus.TeamFormation, true)]
    [InlineData(DraftStatus.ReadyCheck, DraftStatus.Cancelled, true)]
    // SpinnerRanking
    [InlineData(DraftStatus.SpinnerRanking, DraftStatus.ClubSelection, true)]
    [InlineData(DraftStatus.SpinnerRanking, DraftStatus.Cancelled, true)]
    // ClubSelection
    [InlineData(DraftStatus.ClubSelection, DraftStatus.PositionDraft, true)]
    [InlineData(DraftStatus.ClubSelection, DraftStatus.Paused, true)]
    [InlineData(DraftStatus.ClubSelection, DraftStatus.Cancelled, true)]
    // PositionDraft
    [InlineData(DraftStatus.PositionDraft, DraftStatus.Paused, true)]
    [InlineData(DraftStatus.PositionDraft, DraftStatus.Completed, true)]
    [InlineData(DraftStatus.PositionDraft, DraftStatus.Cancelled, true)]
    [InlineData(DraftStatus.PositionDraft, DraftStatus.Abandoned, true)]
    // Paused
    [InlineData(DraftStatus.Paused, DraftStatus.ClubSelection, true)]
    [InlineData(DraftStatus.Paused, DraftStatus.PositionDraft, true)]
    [InlineData(DraftStatus.Paused, DraftStatus.Cancelled, true)]
    [InlineData(DraftStatus.Paused, DraftStatus.Abandoned, true)]
    // Disallowed (a representative spread, including skipped stages and terminal states)
    [InlineData(DraftStatus.Draft, DraftStatus.PositionDraft, false)]
    [InlineData(DraftStatus.Draft, DraftStatus.ReadyCheck, false)]
    [InlineData(DraftStatus.Draft, DraftStatus.Draft, false)]
    [InlineData(DraftStatus.Lobby, DraftStatus.ReadyCheck, false)]
    [InlineData(DraftStatus.Lobby, DraftStatus.Completed, false)]
    [InlineData(DraftStatus.SpinnerRanking, DraftStatus.PositionDraft, false)]
    [InlineData(DraftStatus.SpinnerRanking, DraftStatus.Paused, false)]
    [InlineData(DraftStatus.ReadyCheck, DraftStatus.ClubSelection, false)]
    [InlineData(DraftStatus.Completed, DraftStatus.Lobby, false)]
    [InlineData(DraftStatus.Completed, DraftStatus.Cancelled, false)]
    [InlineData(DraftStatus.Cancelled, DraftStatus.Draft, false)]
    [InlineData(DraftStatus.Abandoned, DraftStatus.PositionDraft, false)]
    public void Transition_allowance_matches_the_state_table(DraftStatus from, DraftStatus to, bool allowed) =>
        Assert.Equal(allowed, DraftStateMachine.IsAllowed(from, to));

    [Theory]
    [InlineData(DraftStatus.Completed)]
    [InlineData(DraftStatus.Cancelled)]
    [InlineData(DraftStatus.Abandoned)]
    public void Terminal_states_allow_no_further_transition(DraftStatus terminal) =>
        Assert.Empty(DraftStateMachine.AllowedFrom(terminal));

    [Fact]
    public void Every_status_has_an_entry_in_the_table()
    {
        foreach (var status in Enum.GetValues<DraftStatus>())
        {
            // AllowedFrom must never throw for a defined status (returns empty for terminals).
            _ = DraftStateMachine.AllowedFrom(status);
        }
    }
}
