using FcDraft.Domain.Entities;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the pure turn-order rules (DRAFT_RULES §5): the club/held round is straight, and the position rounds
/// snake — ascending on odd rounds, descending on even — so PR-14/PR-15/PR-16 can all derive whose turn it is
/// from committed spinner ranks alone.
/// </summary>
public sealed class DraftTurnOrderTests
{
    [Fact]
    public void Straight_order_is_ascending_ranks()
    {
        Assert.Equal(new[] { 1, 2, 3 }, DraftTurnOrder.Straight(3));
    }

    [Theory]
    [InlineData(1, new[] { 1, 2, 3 })] // odd round → ascending
    [InlineData(2, new[] { 3, 2, 1 })] // even round → descending
    [InlineData(3, new[] { 1, 2, 3 })]
    [InlineData(4, new[] { 3, 2, 1 })]
    public void Snake_round_reverses_on_even_rounds(int round, int[] expected)
    {
        Assert.Equal(expected, DraftTurnOrder.SnakeRound(3, round));
    }

    [Theory]
    // 3 teams: round 1 is 1,2,3; round 2 is 3,2,1; round 3 is 1,2,3.
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 2)]
    [InlineData(2, 1, 3)]
    [InlineData(3, 2, 3)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 2, 1)]
    [InlineData(6, 3, 1)]
    public void Next_position_walks_the_snake(int completed, int expectedRound, int expectedRank)
    {
        var (round, rank) = DraftTurnOrder.NextPosition(3, completed);
        Assert.Equal(expectedRound, round);
        Assert.Equal(expectedRank, rank);
    }

    [Fact]
    public void A_full_two_team_snake_visits_every_slot_once_per_team()
    {
        // 2 teams over 15 rounds → each rank appears exactly 15 times, alternating who leads each round.
        var order = Enumerable.Range(0, 30).Select(pick => DraftTurnOrder.NextPosition(2, pick)).ToArray();
        Assert.Equal(15, order.Count(step => step.SpinnerRank == 1));
        Assert.Equal(15, order.Count(step => step.SpinnerRank == 2));
        Assert.Equal((1, 1), order[0]); // round 1 leads with rank 1
        Assert.Equal((2, 2), order[2]); // round 2 leads with rank 2 (snake)
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_draft_needs_at_least_one_team(int teamCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DraftTurnOrder.Straight(teamCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => DraftTurnOrder.NextPosition(teamCount, 0));
    }
}
