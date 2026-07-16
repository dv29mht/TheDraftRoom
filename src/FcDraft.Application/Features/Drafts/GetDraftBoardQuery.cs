using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// The server-authoritative draft board (PR-14/PR-15): whose turn it is plus the eligible options for the
/// current step, all scoped to the pinned dataset and filtered for availability. During club selection it
/// returns the available five-star clubs (and, when <see cref="GetDraftBoardQuery.ClubId"/> is supplied, that
/// club's still-available 75+ players for the held pick); during the position draft it returns the active
/// slot's eligible, still-available pool plus the authoritative pick clock (PR-16). The client polls this
/// for the club/position stages instead of re-deriving snake order, the pools, or the remaining time.
/// </summary>
public sealed record DraftBoardDto(
    string Status,
    DraftTurnDto Turn,
    DraftTimerDto Timer,
    bool IsMyTurn,
    IReadOnlyList<CatalogClub> AvailableClubs,
    IReadOnlyList<CatalogFootballer> EligibleFootballers);

/// <summary>
/// Returns the board for a draft, or null if it does not exist or the caller may not see it.
/// <see cref="Search"/> narrows the eligible pool by name and <see cref="Take"/> deliberately raises the
/// returned pool size (catalog-clamped to 500) — both stay scoped to the pinned dataset, so the room's
/// search never leaves the draft's frozen pool (PR-18, §9.6).
/// </summary>
public sealed record GetDraftBoardQuery(
    Guid DraftId, Guid ActorUserId, bool ActorIsAdmin, Guid? ClubId = null, string? Search = null, int? Take = null)
    : IRequest<DraftBoardDto?>;

public sealed class GetDraftBoardQueryHandler(
    IDraftStore drafts, IDraftCatalog catalog, DraftExpiryService expiry, TimeProvider clock)
    : IRequestHandler<GetDraftBoardQuery, DraftBoardDto?>
{
    public async Task<DraftBoardDto?> Handle(GetDraftBoardQuery request, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindAsync(request.DraftId, cancellationToken);
        if (draft is null)
        {
            return null;
        }

        // Lazy expiry enforcement (PR-16): polling the board applies any overdue auto-pick first, so even
        // with the hosted sweep cold (scale-to-zero) the board never shows a turn that has already expired.
        if (draft.HasExpiredTurn(clock.GetUtcNow()))
        {
            await expiry.CatchUpAsync(draft.Id, cancellationToken);
            draft = await drafts.FindAsync(request.DraftId, cancellationToken) ?? draft;
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
                    draft.PinnedDatasetVersionId,
                    Filter(new CatalogFootballerFilter(ClubId: clubId), request),
                    cancellationToken);
                eligibleFootballers = pool.Where(footballer => !takenFootballers.Contains(footballer.Id)).ToArray();
            }
        }
        else if (draft.Status == DraftStatus.PositionDraft && turn.ActiveSlotOrder is not null)
        {
            var position = turn.SlotAcceptsAnyPosition ? null : turn.ActiveSlotPosition;
            var pool = await catalog.ListFootballersAsync(
                draft.PinnedDatasetVersionId,
                Filter(new CatalogFootballerFilter(Position: position), request),
                cancellationToken);
            eligibleFootballers = pool.Where(footballer => !takenFootballers.Contains(footballer.Id)).ToArray();
        }

        return new DraftBoardDto(
            draft.Status.ToString(), turn, DraftTimer.Describe(draft, clock.GetUtcNow()), isMyTurn,
            availableClubs, eligibleFootballers);
    }

    /// <summary>Folds the caller's search/take into a stage filter; the catalog clamps Take to 1–500.</summary>
    private static CatalogFootballerFilter Filter(CatalogFootballerFilter stage, GetDraftBoardQuery request) =>
        stage with
        {
            Search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            Take = request.Take ?? stage.Take,
        };
}
