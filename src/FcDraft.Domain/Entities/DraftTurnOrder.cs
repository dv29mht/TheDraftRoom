namespace FcDraft.Domain.Entities;

/// <summary>
/// The pure turn-order rules for the draft rounds (DRAFT_RULES §5, decision 5). Turn sequence is a pure
/// function of committed spinner ranks: the pre-draft club/held round is <b>straight</b> (always ascending
/// rank), while every position and bench round is <b>snake</b> — round <c>r</c> (1-indexed) picks in
/// ascending rank when <c>r</c> is odd and descending rank when <c>r</c> is even. Kept in the Domain like
/// <see cref="DraftStateMachine"/> so it is unit-testable and reused by PR-14 (club round), PR-15 (position
/// picks), and PR-16 (auto-pick) without duplicating the rule.
/// </summary>
public static class DraftTurnOrder
{
    /// <summary>The straight order for the pre-draft club/held round: ascending ranks <c>1..teamCount</c>.</summary>
    public static IReadOnlyList<int> Straight(int teamCount)
    {
        EnsurePositive(teamCount);
        return Enumerable.Range(1, teamCount).ToArray();
    }

    /// <summary>
    /// The snake order of spinner ranks for a 1-indexed <paramref name="round"/>: ascending on odd rounds,
    /// descending on even rounds. Round 1 is <c>1,2,…,n</c>; round 2 is <c>n,…,2,1</c>; and so on.
    /// </summary>
    public static IReadOnlyList<int> SnakeRound(int teamCount, int round)
    {
        EnsurePositive(teamCount);
        if (round < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(round), round, "A round is 1-indexed.");
        }

        var ranks = Enumerable.Range(1, teamCount);
        return (round % 2 == 1 ? ranks : ranks.Reverse()).ToArray();
    }

    /// <summary>
    /// The (1-indexed round, spinner rank) of the next position pick, given how many position picks are
    /// already complete across all teams. Each round fills one slot for every team in snake order, so the
    /// round advances every <paramref name="teamCount"/> picks. Reused by the pick engine to decide whose
    /// turn it is and (PR-16) which slot the timer's auto-pick fills.
    /// </summary>
    public static (int Round, int SpinnerRank) NextPosition(int teamCount, int completedPositionPicks)
    {
        EnsurePositive(teamCount);
        if (completedPositionPicks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedPositionPicks), completedPositionPicks, "The completed count cannot be negative.");
        }

        var round = completedPositionPicks / teamCount + 1;
        var indexInRound = completedPositionPicks % teamCount;
        return (round, SnakeRound(teamCount, round)[indexInRound]);
    }

    private static void EnsurePositive(int teamCount)
    {
        if (teamCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(teamCount), teamCount, "A draft has at least one team.");
        }
    }
}
