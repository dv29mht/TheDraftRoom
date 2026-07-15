using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>Returns the full, enriched lobby snapshot (participants, capacity, event history), or null if not found.</summary>
public sealed record GetDraftQuery(Guid DraftId) : IRequest<DraftDetail?>;

/// <summary>
/// Lists the drafts visible to a caller, newest first: an admin sees every draft; anyone else sees the
/// drafts they host or are a participant of (PRD §8 — the draft hub is per-user).
/// </summary>
public sealed record ListDraftsQuery(Guid ActorUserId, bool ActorIsAdmin = false) : IRequest<IReadOnlyList<DraftSummary>>;

/// <summary>Lists the active accounts a host may invite to a lobby (everyone except the host themselves).</summary>
public sealed record ListInvitableUsersQuery(Guid ActorUserId, string? Search = null)
    : IRequest<IReadOnlyList<InvitableUserDto>>;

public sealed class GetDraftQueryHandler(IDraftStore drafts, IIdentityService identity)
    : IRequestHandler<GetDraftQuery, DraftDetail?>
{
    public async Task<DraftDetail?> Handle(GetDraftQuery request, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindAsync(request.DraftId, cancellationToken);
        return draft is null ? null : await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }
}

public sealed class ListDraftsQueryHandler(IDraftStore drafts)
    : IRequestHandler<ListDraftsQuery, IReadOnlyList<DraftSummary>>
{
    public async Task<IReadOnlyList<DraftSummary>> Handle(ListDraftsQuery request, CancellationToken cancellationToken)
    {
        var all = await drafts.ListAsync(cancellationToken);
        var visible = request.ActorIsAdmin
            ? all
            : all.Where(draft =>
                draft.HostUserId == request.ActorUserId
                || draft.Participants.Any(participant => participant.UserId == request.ActorUserId));
        return visible.Select(DraftMapper.ToSummary).ToArray();
    }
}

public sealed class ListInvitableUsersQueryHandler(IIdentityService identity)
    : IRequestHandler<ListInvitableUsersQuery, IReadOnlyList<InvitableUserDto>>
{
    // The directory is small (a private app), but page through so a large roster is never silently
    // truncated. Only active accounts other than the caller can be invited.
    private const int PageSize = 50;

    public async Task<IReadOnlyList<InvitableUserDto>> Handle(
        ListInvitableUsersQuery request, CancellationToken cancellationToken)
    {
        var results = new List<InvitableUserDto>();
        var page = 1;
        while (true)
        {
            var directory = await identity.SearchUsersAsync(
                new UserDirectoryQuery(request.Search, page, PageSize), cancellationToken);

            results.AddRange(directory.Items
                .Where(user => user.Status == AccountStatus.Active && user.Id != request.ActorUserId)
                .Select(user => new InvitableUserDto(user.Id, user.DisplayName, user.Email)));

            if (page >= directory.TotalPages || directory.Items.Count == 0)
            {
                break;
            }

            page++;
        }

        return results;
    }
}
