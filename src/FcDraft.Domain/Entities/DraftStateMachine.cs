namespace FcDraft.Domain.Entities;

/// <summary>
/// The allowed draft state transitions (PRD §10.1). This is the single source of truth for which moves
/// are legal; command handlers pre-check with <see cref="IsAllowed"/> and the <see cref="Draft"/>
/// aggregate guards with it defensively. Terminal states (Completed, Cancelled, Abandoned) allow none.
/// </summary>
public static class DraftStateMachine
{
    private static readonly IReadOnlyDictionary<DraftStatus, IReadOnlyList<DraftStatus>> Transitions =
        new Dictionary<DraftStatus, IReadOnlyList<DraftStatus>>
        {
            [DraftStatus.Draft] = new[] { DraftStatus.Lobby, DraftStatus.Cancelled },
            [DraftStatus.Lobby] = new[] { DraftStatus.TeamFormation, DraftStatus.Cancelled },
            [DraftStatus.TeamFormation] = new[] { DraftStatus.ReadyCheck, DraftStatus.Lobby, DraftStatus.Cancelled },
            [DraftStatus.ReadyCheck] = new[] { DraftStatus.SpinnerRanking, DraftStatus.TeamFormation, DraftStatus.Cancelled },
            [DraftStatus.SpinnerRanking] = new[] { DraftStatus.ClubSelection, DraftStatus.Cancelled },
            [DraftStatus.ClubSelection] = new[] { DraftStatus.PositionDraft, DraftStatus.Paused, DraftStatus.Cancelled },
            [DraftStatus.PositionDraft] = new[] { DraftStatus.Paused, DraftStatus.Completed, DraftStatus.Cancelled, DraftStatus.Abandoned },
            [DraftStatus.Paused] = new[] { DraftStatus.ClubSelection, DraftStatus.PositionDraft, DraftStatus.Cancelled, DraftStatus.Abandoned },
            [DraftStatus.Completed] = Array.Empty<DraftStatus>(),
            [DraftStatus.Cancelled] = Array.Empty<DraftStatus>(),
            [DraftStatus.Abandoned] = Array.Empty<DraftStatus>(),
        };

    /// <summary>The states reachable in one step from <paramref name="status"/> (empty for terminal states).</summary>
    public static IReadOnlyList<DraftStatus> AllowedFrom(DraftStatus status) =>
        Transitions.TryGetValue(status, out var next) ? next : Array.Empty<DraftStatus>();

    public static bool IsAllowed(DraftStatus from, DraftStatus to) => AllowedFrom(from).Contains(to);
}

/// <summary>Thrown by the <see cref="Draft"/> aggregate when asked to make a transition §10.1 forbids.</summary>
public sealed class InvalidDraftTransitionException(DraftStatus from, DraftStatus to)
    : Exception($"A draft cannot move from {from} to {to}.")
{
    public DraftStatus From { get; } = from;
    public DraftStatus To { get; } = to;
}

/// <summary>
/// Rebuilds a draft's current status and version purely from its append-only event history, proving the
/// PR-10 done-when "current state can be rebuilt or verified from history". Validates that the history
/// begins with <see cref="DraftEventType.DraftCreated"/> and has a contiguous sequence with no gaps.
/// </summary>
public static class DraftStateProjection
{
    public static DraftStateSnapshot Replay(IEnumerable<DraftEvent> events)
    {
        var ordered = events.OrderBy(evt => evt.Sequence).ToArray();
        if (ordered.Length == 0)
        {
            throw new InvalidOperationException("A draft history must contain at least the DraftCreated event.");
        }
        if (ordered[0].Type != DraftEventType.DraftCreated)
        {
            throw new InvalidOperationException("A draft history must begin with the DraftCreated event.");
        }

        var status = DraftStatus.Draft;
        var version = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var evt = ordered[index];
            if (evt.Sequence != index + 1)
            {
                throw new InvalidOperationException($"A draft history has a gap or duplicate at sequence {index + 1}.");
            }
            if (evt.ToStatus is { } to)
            {
                status = to;
            }
            version = evt.Version;
        }

        return new DraftStateSnapshot(status, version, ordered.Length);
    }
}

/// <summary>The status and version reconstructed from a draft's event history (see <see cref="DraftStateProjection"/>).</summary>
public sealed record DraftStateSnapshot(DraftStatus Status, int Version, int EventCount);
