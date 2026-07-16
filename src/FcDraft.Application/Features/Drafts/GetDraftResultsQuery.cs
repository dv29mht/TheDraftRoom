using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// One pick within a completed draft's result (PR-19, §9.7). <see cref="Sequence"/> is its 1-based place in
/// the draft's chronological pick order. Identity, rating, and position come from the FROZEN pick row;
/// club/league/nation are display-only extras resolved from the pinned dataset version (whose rows never
/// change after import), so a later dataset activation cannot alter what is shown.
/// </summary>
public sealed record ResultPickDto(
    int Sequence,
    Guid TeamId,
    int SlotOrder,
    string SlotLabel,
    string? SlotPosition,
    int FootballerId,
    string FootballerName,
    int FootballerOverall,
    string? FootballerPosition,
    string? ClubName,
    string? League,
    string? Nation);

/// <summary>Average rating of one line of the starting XI, derived from the frozen slot positions.</summary>
public sealed record LineRatingDto(string Line, double? Average, int Filled, int SlotCount);

public sealed record TeamResultDto(
    Guid TeamId,
    string Name,
    int? SpinnerRank,
    string? SelectedClubName,
    IReadOnlyList<Guid> MemberUserIds,
    IReadOnlyList<string> MemberNames,
    double? AverageOverall,
    IReadOnlyList<LineRatingDto> LineRatings,
    IReadOnlyList<string> Clubs,
    IReadOnlyList<string> Leagues,
    IReadOnlyList<string> Nations,
    IReadOnlyList<ResultPickDto> Picks);

public sealed record DraftResultsDto(
    DraftSummary Summary,
    IReadOnlyList<DraftRosterSlotDto> Slots,
    IReadOnlyList<TeamResultDto> Teams,
    IReadOnlyList<ResultPickDto> PickSequence);

/// <summary>
/// Returns a COMPLETED draft's results, or null when the draft does not exist, the caller may not see it
/// (the 404-not-403 rule of every draft read), or the draft has not completed. §9.7's immutability holds
/// without a dedicated snapshot table: picks/slots were frozen and denormalized at pick time (PR-15), and
/// the display-only extras read from the pinned dataset version, which is itself immutable after import.
/// </summary>
public sealed record GetDraftResultsQuery(Guid DraftId, Guid ActorUserId, bool ActorIsAdmin)
    : IRequest<DraftResultsDto?>;

public sealed class GetDraftResultsQueryHandler(IDraftStore drafts, IDraftCatalog catalog, IIdentityService identity)
    : IRequestHandler<GetDraftResultsQuery, DraftResultsDto?>
{
    public async Task<DraftResultsDto?> Handle(GetDraftResultsQuery request, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindAsync(request.DraftId, cancellationToken);
        if (draft is null || draft.Status != DraftStatus.Completed)
        {
            return null;
        }

        var isParticipant = draft.Participants.Any(participant => participant.UserId == request.ActorUserId);
        if (!request.ActorIsAdmin && draft.HostUserId != request.ActorUserId && !isParticipant)
        {
            return null;
        }

        var facts = await catalog.MapFootballerFactsAsync(
            draft.PinnedDatasetVersionId,
            draft.Picks.Select(pick => pick.FootballerId).Distinct().ToArray(),
            cancellationToken);
        var clubNames = await ClubNamesAsync(draft, cancellationToken);
        var memberNames = await MemberNamesAsync(draft, cancellationToken);

        var slots = draft.Slots.OrderBy(slot => slot.Order).ToArray();
        var slotByOrder = slots.ToDictionary(slot => slot.Order);
        var sequence = ChronologicalPicks(draft)
            .Select((pick, index) => ToResultPick(pick, index + 1, slotByOrder, facts))
            .ToArray();

        var userIdByParticipantId = draft.Participants.ToDictionary(participant => participant.Id, participant => participant.UserId);
        var teams = draft.Teams
            .OrderBy(team => team.SpinnerRank ?? int.MaxValue)
            .ThenBy(team => team.Name)
            .Select(team =>
            {
                var picks = sequence.Where(pick => pick.TeamId == team.Id).OrderBy(pick => pick.SlotOrder).ToArray();
                var memberUserIds = team.Members
                    .Select(member => userIdByParticipantId.TryGetValue(member.ParticipantId, out var userId) ? userId : member.ParticipantId)
                    .ToArray();
                // The club name resolves from the held pick's immutable dataset facts first — the five-star
                // flag is admin-mutable, so the FindFiveStarClub lookup is only a fallback.
                var selectedClubName = team.SelectedClubId is { } clubId
                    ? picks.FirstOrDefault(pick => pick.SlotOrder == Draft.HeldSlotOrder)?.ClubName
                        ?? clubNames.GetValueOrDefault(clubId)
                    : null;
                return new TeamResultDto(
                    team.Id,
                    team.Name,
                    team.SpinnerRank,
                    selectedClubName,
                    memberUserIds,
                    memberUserIds.Select(userId => memberNames.GetValueOrDefault(userId, "Unknown player")).ToArray(),
                    picks.Length == 0 ? null : Math.Round(picks.Average(pick => pick.FootballerOverall), 1),
                    LineRatings(slots, picks),
                    picks.Select(pick => pick.ClubName).OfType<string>().Distinct().OrderBy(name => name).ToArray(),
                    picks.Select(pick => pick.League).OfType<string>().Distinct().OrderBy(name => name).ToArray(),
                    picks.Select(pick => pick.Nation).OfType<string>().Distinct().OrderBy(name => name).ToArray(),
                    picks);
            })
            .ToArray();

        return new DraftResultsDto(
            DraftMapper.ToSummary(draft),
            slots.Select(slot => new DraftRosterSlotDto(slot.Order, slot.SlotType.ToString(), slot.Position, slot.Label)).ToArray(),
            teams,
            sequence);
    }

    /// <summary>
    /// The chronological pick order, re-derived from the deterministic turn rules over the frozen picks:
    /// the held round (slot 0) ran straight by spinner rank; each position round r (slot order r) snaked —
    /// odd rounds ascend by rank, even rounds descend. This IS the order the server accepted the picks in
    /// (chosen over CreatedAt, whose sub-second ties would make ordering less deterministic).
    /// </summary>
    internal static IEnumerable<DraftPick> ChronologicalPicks(Draft draft)
    {
        var rankByTeam = draft.Teams.ToDictionary(team => team.Id, team => team.SpinnerRank ?? int.MaxValue);
        return draft.Picks
            .OrderBy(pick => pick.SlotOrder)
            .ThenBy(pick => pick.SlotOrder == Draft.HeldSlotOrder || pick.SlotOrder % 2 == 1
                ? rankByTeam.GetValueOrDefault(pick.DraftTeamId, int.MaxValue)
                : -rankByTeam.GetValueOrDefault(pick.DraftTeamId, int.MaxValue));
    }

    /// <summary>Maps a starting-XI slot position to its line; held/flex-bench slots carry no line.</summary>
    internal static string? LineOf(string? slotPosition) => slotPosition switch
    {
        "GK" => "GK",
        "LB" or "CB" or "RB" or "LWB" or "RWB" => "DEF",
        "CM" or "CDM" or "CAM" or "LM" or "RM" => "MID",
        "ST" or "CF" or "LW" or "RW" => "FWD",
        _ => null,
    };

    private static LineRatingDto[] LineRatings(IReadOnlyList<DraftRosterSlot> slots, IReadOnlyList<ResultPickDto> picks)
    {
        var pickBySlot = picks.ToDictionary(pick => pick.SlotOrder);
        return new[] { "GK", "DEF", "MID", "FWD" }
            .Select(line =>
            {
                var lineSlots = slots.Where(slot => LineOf(slot.Position) == line).ToArray();
                var filled = lineSlots
                    .Select(slot => pickBySlot.TryGetValue(slot.Order, out var pick) ? pick : null)
                    .OfType<ResultPickDto>()
                    .ToArray();
                return new LineRatingDto(
                    line,
                    filled.Length == 0 ? null : Math.Round(filled.Average(pick => pick.FootballerOverall), 1),
                    filled.Length,
                    lineSlots.Length);
            })
            .ToArray();
    }

    private static ResultPickDto ToResultPick(
        DraftPick pick, int sequence, IReadOnlyDictionary<int, DraftRosterSlot> slotByOrder,
        IReadOnlyDictionary<int, CatalogFootballerFacts> facts)
    {
        var slot = slotByOrder.GetValueOrDefault(pick.SlotOrder);
        var extra = facts.GetValueOrDefault(pick.FootballerId);
        return new ResultPickDto(
            sequence,
            pick.DraftTeamId,
            pick.SlotOrder,
            slot?.Label ?? (pick.SlotOrder == Draft.HeldSlotOrder ? "Held player" : $"Slot {pick.SlotOrder}"),
            slot?.Position,
            pick.FootballerId,
            pick.FootballerName,
            pick.FootballerOverall,
            pick.FootballerPosition,
            extra?.ClubName,
            extra?.League,
            extra?.Nation);
    }

    private async Task<Dictionary<Guid, string>> ClubNamesAsync(Draft draft, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var clubId in draft.Teams.Where(team => team.SelectedClubId is not null).Select(team => team.SelectedClubId!.Value).Distinct())
        {
            var club = await catalog.FindFiveStarClubAsync(draft.PinnedDatasetVersionId, clubId, cancellationToken);
            if (club is not null)
            {
                names[clubId] = club.Name;
            }
        }

        return names;
    }

    private async Task<Dictionary<Guid, string>> MemberNamesAsync(Draft draft, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var userId in draft.Participants.Select(participant => participant.UserId).Distinct())
        {
            var user = await identity.FindByIdAsync(userId, cancellationToken);
            if (user is not null)
            {
                names[userId] = user.DisplayName;
            }
        }

        return names;
    }
}
