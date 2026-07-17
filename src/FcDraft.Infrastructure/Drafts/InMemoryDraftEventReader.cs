using FcDraft.Application.Common.Interfaces;

namespace FcDraft.Infrastructure.Drafts;

/// <summary>
/// Audit queries over the in-memory draft aggregates' event histories (PR-21). Reads through
/// <see cref="IDraftStore"/> — the same aggregates the commands mutate — so it sees exactly the
/// events the append-only trail holds for this process.
/// </summary>
public sealed class InMemoryDraftEventReader(IDraftStore drafts) : IDraftEventReader
{
    public async Task<IReadOnlyList<DraftEventRecord>> QueryAsync(
        DraftEventQuery query, CancellationToken cancellationToken)
    {
        var all = await drafts.ListAsync(cancellationToken);

        var events = all
            .Where(draft => !query.DraftId.HasValue || draft.Id == query.DraftId.Value)
            .SelectMany(draft => draft.Events.Select(evt => new { draft, evt }))
            .Where(joined => !query.Type.HasValue || joined.evt.Type == query.Type.Value)
            .Where(joined => !query.ActorUserId.HasValue || joined.evt.ActorUserId == query.ActorUserId.Value)
            .Where(joined => !query.From.HasValue || joined.evt.CreatedAt >= query.From.Value)
            .Where(joined => !query.To.HasValue || joined.evt.CreatedAt <= query.To.Value)
            .OrderByDescending(joined => joined.evt.CreatedAt)
            .ThenByDescending(joined => joined.evt.Sequence)
            .Take(query.Take)
            .Select(joined => new DraftEventRecord(
                joined.draft.Id,
                joined.draft.Name,
                joined.draft.Code,
                joined.evt.Sequence,
                joined.evt.Type.ToString(),
                joined.evt.FromStatus?.ToString(),
                joined.evt.ToStatus?.ToString(),
                joined.evt.Version,
                joined.evt.ActorUserId,
                joined.evt.Reason,
                joined.evt.CreatedAt))
            .ToArray();

        return events;
    }
}
