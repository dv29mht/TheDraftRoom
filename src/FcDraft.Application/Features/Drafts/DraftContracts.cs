using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;

namespace FcDraft.Application.Features.Drafts;

public sealed record DraftSummary(
    Guid Id,
    string Code,
    string Name,
    string Format,
    string Status,
    Guid HostUserId,
    int Version,
    int PickTimerSeconds,
    Guid? PinnedDatasetVersionId,
    int ParticipantCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// A lobby participant. <see cref="DisplayName"/>/<see cref="Email"/> are resolved from the identity
/// store when the detail is projected so the lobby UI can render who is invited/joined without a second
/// round-trip; they are null when the account can no longer be resolved.
/// </summary>
public sealed record DraftParticipantDto(
    Guid UserId, string? DisplayName, string? Email, bool IsHost, string? Seed, string Status, bool IsReady);

public sealed record DraftTeamDto(
    Guid Id, string Name, int? SpinnerRank, Guid? SelectedClubId, IReadOnlyList<Guid> MemberParticipantIds);

public sealed record DraftRosterSlotDto(int Order, string SlotType, string? Position, string Label);

public sealed record DraftEventDto(
    int Sequence,
    string Type,
    string? FromStatus,
    string? ToStatus,
    int Version,
    Guid? ActorUserId,
    string? Reason,
    DateTimeOffset CreatedAt);

/// <summary>
/// The server-authoritative capacity picture of a lobby (PRD §6.2, §9.4). The client renders this instead
/// of re-deriving the rules; the same rules are enforced server-side on invite (max) and lock (min/max/even).
/// </summary>
public sealed record LobbyCapacityDto(
    int Min,
    int Max,
    bool RequiresEven,
    int ParticipantCount,
    int JoinedCount,
    int InvitedCount,
    bool MeetsMinimum,
    bool WithinMaximum,
    bool MeetsEven,
    bool CanLock);

public sealed record DraftDetail(
    DraftSummary Summary,
    LobbyCapacityDto Capacity,
    IReadOnlyList<DraftParticipantDto> Participants,
    IReadOnlyList<DraftTeamDto> Teams,
    IReadOnlyList<DraftRosterSlotDto> Slots,
    IReadOnlyList<DraftEventDto> Events);

/// <summary>A user the host may invite to a lobby: an active account other than the host itself.</summary>
public sealed record InvitableUserDto(Guid Id, string DisplayName, string Email);

/// <summary>Maps the wire format string ("1v1" / "2v2") to and from <see cref="DraftFormat"/>.</summary>
public static class DraftFormats
{
    public static bool TryParse(string? value, out DraftFormat format)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "1v1":
                format = DraftFormat.OneVsOne;
                return true;
            case "2v2":
                format = DraftFormat.TwoVsTwo;
                return true;
            default:
                format = default;
                return false;
        }
    }

    public static string ToWire(DraftFormat format) => format == DraftFormat.TwoVsTwo ? "2v2" : "1v1";
}

/// <summary>
/// The lobby capacity rules (PRD §6.2 / §9.4): 1v1 allows 2–10 human participants; 2v2 allows 4–16 and
/// the count must be even. The max is enforced as each invite is accepted; the minimum and even-count are
/// enforced when the host locks the lobby into team formation.
/// </summary>
public static class LobbyCapacity
{
    public static (int Min, int Max) Bounds(DraftFormat format) =>
        format == DraftFormat.TwoVsTwo ? (4, 16) : (2, 10);

    public static bool RequiresEven(DraftFormat format) => format == DraftFormat.TwoVsTwo;

    /// <summary>True when adding one more participant would keep the count within the format's maximum.</summary>
    public static bool CanAdmitAnother(DraftFormat format, int currentCount) =>
        currentCount + 1 <= Bounds(format).Max;

    /// <summary>True when the count satisfies min, max, and (for 2v2) the even-count rule.</summary>
    public static bool IsLockable(DraftFormat format, int count)
    {
        var (min, max) = Bounds(format);
        return count >= min && count <= max && (!RequiresEven(format) || count % 2 == 0);
    }

    public static LobbyCapacityDto Describe(Draft draft)
    {
        var (min, max) = Bounds(draft.Format);
        var count = draft.Participants.Count;
        var joined = draft.Participants.Count(participant => participant.Status == DraftParticipantStatus.Joined);
        var even = !RequiresEven(draft.Format) || count % 2 == 0;
        return new LobbyCapacityDto(
            min,
            max,
            RequiresEven(draft.Format),
            count,
            joined,
            count - joined,
            count >= min,
            count <= max,
            even,
            draft.Status == DraftStatus.Lobby && IsLockable(draft.Format, count));
    }
}

/// <summary>Projects the draft aggregate to the API/read contracts. Shared by the command and query handlers.</summary>
public static class DraftMapper
{
    public static DraftSummary ToSummary(Draft draft) => new(
        draft.Id,
        draft.Code,
        draft.Name,
        DraftFormats.ToWire(draft.Format),
        draft.Status.ToString(),
        draft.HostUserId,
        draft.Version,
        draft.PickTimerSeconds,
        draft.PinnedDatasetVersionId,
        draft.Participants.Count,
        draft.CreatedAt,
        draft.StartedAt,
        draft.CompletedAt);

    /// <summary>
    /// Projects the full lobby snapshot. <paramref name="identities"/> supplies each participant's display
    /// name/email (resolved by the handler); when absent those fields project as null.
    /// </summary>
    public static DraftDetail ToDetail(
        Draft draft,
        IReadOnlyDictionary<Guid, (string DisplayName, string Email)>? identities = null) => new(
        ToSummary(draft),
        LobbyCapacity.Describe(draft),
        draft.Participants
            .OrderByDescending(participant => participant.IsHost)
            .ThenBy(participant => participant.CreatedAt)
            .Select(participant =>
            {
                (string DisplayName, string Email)? identity =
                    identities is not null && identities.TryGetValue(participant.UserId, out var found) ? found : null;
                return new DraftParticipantDto(
                    participant.UserId,
                    identity?.DisplayName,
                    identity?.Email,
                    participant.IsHost,
                    participant.Seed?.ToString(),
                    participant.Status.ToString(),
                    participant.IsReady);
            })
            .ToArray(),
        draft.Teams
            .OrderBy(team => team.SpinnerRank ?? int.MaxValue)
            .ThenBy(team => team.Name)
            .Select(team => new DraftTeamDto(
                team.Id,
                team.Name,
                team.SpinnerRank,
                team.SelectedClubId,
                team.Members.Select(member => member.ParticipantId).ToArray()))
            .ToArray(),
        draft.Slots
            .OrderBy(slot => slot.Order)
            .Select(slot => new DraftRosterSlotDto(slot.Order, slot.SlotType.ToString(), slot.Position, slot.Label))
            .ToArray(),
        draft.Events
            .OrderBy(evt => evt.Sequence)
            .Select(evt => new DraftEventDto(
                evt.Sequence,
                evt.Type.ToString(),
                evt.FromStatus?.ToString(),
                evt.ToStatus?.ToString(),
                evt.Version,
                evt.ActorUserId,
                evt.Reason,
                evt.CreatedAt))
            .ToArray());
}

/// <summary>
/// Builds the enriched lobby <see cref="DraftDetail"/> by resolving each participant's account from the
/// identity store. Shared by every draft command/query handler that returns a lobby snapshot so participant
/// names are consistently populated. Lobbies are small (≤16), so the per-participant lookups are cheap.
/// </summary>
internal static class LobbyProjection
{
    public static async Task<DraftDetail> ToDetailAsync(
        Draft draft, IIdentityService identity, CancellationToken cancellationToken)
    {
        var identities = new Dictionary<Guid, (string DisplayName, string Email)>();
        foreach (var userId in draft.Participants.Select(participant => participant.UserId).Distinct())
        {
            var user = await identity.FindByIdAsync(userId, cancellationToken);
            if (user is not null)
            {
                identities[userId] = (user.DisplayName, user.Email);
            }
        }

        return DraftMapper.ToDetail(draft, identities);
    }
}

/// <summary>Shared authorization and concurrency guards for the draft command handlers.</summary>
internal static class DraftGuards
{
    public static void EnsureActorMayControl(Draft draft, Guid actorUserId, bool actorIsAdmin)
    {
        if (!actorIsAdmin && draft.HostUserId != actorUserId)
        {
            throw new Common.Exceptions.ForbiddenAppException(
                "Only the draft host or an administrator can control this draft.");
        }
    }

    public static void EnsureExpectedVersion(Draft draft, int expectedVersion)
    {
        if (draft.Version != expectedVersion)
        {
            throw new Common.Exceptions.ConflictAppException(
                $"This draft has moved on (it is now version {draft.Version}, you sent {expectedVersion}). Refresh and try again.");
        }
    }
}

/// <summary>Generates a short, human-shareable draft code (mirrors the legacy room code).</summary>
internal static class DraftCode
{
    public static string New() => Convert.ToHexString(Guid.NewGuid().ToByteArray())[..6];
}
