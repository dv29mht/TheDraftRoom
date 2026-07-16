using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// The server-authoritative draft board (PR-14/PR-15): whose turn it is plus the eligible options for the
/// current step, all scoped to the pinned dataset and filtered for availability. During club selection it
/// returns the available five-star clubs (and, when <see cref="GetDraftBoardQuery.ClubId"/> is supplied, that
/// club's still-available 75+ players for the held pick); during the position draft it returns the active
/// slot's eligible, still-available pool. The client polls this for the club/position stages instead of
/// re-deriving snake order or the pools.
/// </summary>
public sealed record DraftBoardDto(
    string Status,
    DraftTurnDto Turn,
    bool IsMyTurn,
    IReadOnlyList<CatalogClub> AvailableClubs,
    IReadOnlyList<CatalogFootballer> EligibleFootballers);

/// <summary>Returns the board for a draft, or null if it does not exist or the caller may not see it.</summary>
public sealed record GetDraftBoardQuery(Guid DraftId, Guid ActorUserId, bool ActorIsAdmin, Guid? ClubId = null)
    : IRequest<DraftBoardDto?>;

public sealed class GetDraftBoardQueryHandler(IDraftStore drafts, IDraftCatalog catalog)
    : IRequestHandler<GetDraftBoardQuery, DraftBoardDto?>
{
    public async Task<DraftBoardDto?> Handle(GetDraftBoardQuery request, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindAsync(request.DraftId, cancellationToken);
        if (draft is null)
        {
            return null;
        }

        // 404 (via null) rather than 403 for non-participants, matching GET /drafts/{id}.
        var isParticipant = draft.Participants.Any(participant => participant.UserId == request.ActorUserId);
        if (!request.ActorIsAdmin && draft.HostUserId != request.ActorUserId && !isParticipant)
        {
            return null;
        }

        var turn = DraftTurn.Describe(draft);
        var isMyTurn = turn.ActiveTeamMemberUserIds.Contains(request.ActorUserId);
        var takenFootballers = draft.Picks.Select(pick => pick.FootballerId).ToHashSet();

        IReadOnlyList<CatalogClub> availableClubs = [];
        IReadOnlyList<CatalogFootballer> eligibleFootballers = [];

        if (draft.Status == DraftStatus.ClubSelection)
        {
            var takenClubs = draft.Teams
                .Where(team => team.SelectedClubId is not null)
                .Select(team => team.SelectedClubId!.Value)
                .ToHashSet();
            var clubs = await catalog.ListFiveStarClubsAsync(draft.PinnedDatasetVersionId, cancellationToken);
            availableClubs = clubs.Where(club => !takenClubs.Contains(club.Id)).ToArray();

            // The held pool is that chosen club's still-available 75+ players (fetched once the user picks a club).
            if (request.ClubId is { } clubId)
            {
                var pool = await catalog.ListFootballersAsync(
                    draft.PinnedDatasetVersionId, new CatalogFootballerFilter(ClubId: clubId), cancellationToken);
                eligibleFootballers = pool.Where(footballer => !takenFootballers.Contains(footballer.Id)).ToArray();
            }
        }
        else if (draft.Status == DraftStatus.PositionDraft && turn.ActiveSlotOrder is not null)
        {
            var position = turn.SlotAcceptsAnyPosition ? null : turn.ActiveSlotPosition;
            var pool = await catalog.ListFootballersAsync(
                draft.PinnedDatasetVersionId, new CatalogFootballerFilter(Position: position), cancellationToken);
            eligibleFootballers = pool.Where(footballer => !takenFootballers.Contains(footballer.Id)).ToArray();
        }

        return new DraftBoardDto(draft.Status.ToString(), turn, isMyTurn, availableClubs, eligibleFootballers);
    }
}
