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

public sealed record DraftParticipantDto(Guid UserId, bool IsHost, string? Seed, string Status, bool IsReady);

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

public sealed record DraftDetail(
    DraftSummary Summary,
    IReadOnlyList<DraftParticipantDto> Participants,
    IReadOnlyList<DraftTeamDto> Teams,
    IReadOnlyList<DraftRosterSlotDto> Slots,
    IReadOnlyList<DraftEventDto> Events);

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

    public static DraftDetail ToDetail(Draft draft) => new(
        ToSummary(draft),
        draft.Participants
            .OrderBy(participant => participant.CreatedAt)
            .Select(participant => new DraftParticipantDto(
                participant.UserId,
                participant.IsHost,
                participant.Seed?.ToString(),
                participant.Status.ToString(),
                participant.IsReady))
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
