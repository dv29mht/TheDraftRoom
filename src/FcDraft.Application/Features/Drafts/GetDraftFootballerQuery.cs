using FcDraft.Application.Common.Interfaces;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// One footballer's full §9.6 detail card inside a draft (PR-18): the pinned-dataset card (stats, roles
/// with +/++, PlayStyles, club/league/nation) plus this draft's availability — whether the footballer is
/// already held or drafted, and by which team into which slot, so an unavailable player is understandable
/// rather than silently missing from the pool.
/// </summary>
public sealed record DraftFootballerDto(
    CatalogFootballerCard Card,
    bool IsTaken,
    Guid? TakenByTeamId,
    string? TakenByTeamName,
    string? TakenSlotLabel);

/// <summary>
/// Returns the card, or null when the draft does not exist, the caller may not see it (404-not-403, like
/// every other draft read), or the footballer is not part of the draft's pinned eligible pool.
/// </summary>
public sealed record GetDraftFootballerQuery(Guid DraftId, int FootballerId, Guid ActorUserId, bool ActorIsAdmin)
    : IRequest<DraftFootballerDto?>;

public sealed class GetDraftFootballerQueryHandler(IDraftStore drafts, IDraftCatalog catalog)
    : IRequestHandler<GetDraftFootballerQuery, DraftFootballerDto?>
{
    public async Task<DraftFootballerDto?> Handle(GetDraftFootballerQuery request, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindAsync(request.DraftId, cancellationToken);
        if (draft is null)
        {
            return null;
        }

        var isParticipant = draft.Participants.Any(participant => participant.UserId == request.ActorUserId);
        if (!request.ActorIsAdmin && draft.HostUserId != request.ActorUserId && !isParticipant)
        {
            return null;
        }

        var card = await catalog.FindFootballerCardAsync(draft.PinnedDatasetVersionId, request.FootballerId, cancellationToken);
        if (card is null)
        {
            return null;
        }

        var pick = draft.Picks.FirstOrDefault(candidate => candidate.FootballerId == request.FootballerId);
        if (pick is null)
        {
            return new DraftFootballerDto(card, IsTaken: false, null, null, null);
        }

        var team = draft.Teams.FirstOrDefault(candidate => candidate.Id == pick.DraftTeamId);
        var slot = draft.Slots.FirstOrDefault(candidate => candidate.Order == pick.SlotOrder);
        return new DraftFootballerDto(card, IsTaken: true, pick.DraftTeamId, team?.Name, slot?.Label);
    }
}
