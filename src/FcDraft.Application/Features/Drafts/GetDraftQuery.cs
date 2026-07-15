using FcDraft.Application.Common.Interfaces;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>Returns the full draft snapshot including its append-only event history, or null if not found.</summary>
public sealed record GetDraftQuery(Guid DraftId) : IRequest<DraftDetail?>;

/// <summary>Lists draft summaries, newest first. Participant/host scoping is added with the lobby in PR-11.</summary>
public sealed record ListDraftsQuery() : IRequest<IReadOnlyList<DraftSummary>>;

public sealed class GetDraftQueryHandler(IDraftStore drafts) : IRequestHandler<GetDraftQuery, DraftDetail?>
{
    public async Task<DraftDetail?> Handle(GetDraftQuery request, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindAsync(request.DraftId, cancellationToken);
        return draft is null ? null : DraftMapper.ToDetail(draft);
    }
}

public sealed class ListDraftsQueryHandler(IDraftStore drafts) : IRequestHandler<ListDraftsQuery, IReadOnlyList<DraftSummary>>
{
    public async Task<IReadOnlyList<DraftSummary>> Handle(ListDraftsQuery request, CancellationToken cancellationToken)
    {
        var all = await drafts.ListAsync(cancellationToken);
        return all.Select(DraftMapper.ToSummary).ToArray();
    }
}
