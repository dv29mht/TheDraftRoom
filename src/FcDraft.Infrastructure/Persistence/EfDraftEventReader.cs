using FcDraft.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Read-only audit queries over the append-only <c>draft_events</c> table across all drafts (PR-21).
/// Filters run in the database; nothing here can write — the aggregate is the only writer and only
/// ever appends.
/// </summary>
public sealed class EfDraftEventReader(FcDraftDbContext dbContext) : IDraftEventReader
{
    public async Task<IReadOnlyList<DraftEventRecord>> QueryAsync(
        DraftEventQuery query, CancellationToken cancellationToken)
    {
        var events = dbContext.DraftEvents.AsNoTracking();

        if (query.DraftId.HasValue)
        {
            events = events.Where(evt => evt.DraftId == query.DraftId.Value);
        }

        if (query.Type.HasValue)
        {
            events = events.Where(evt => evt.Type == query.Type.Value);
        }

        if (query.ActorUserId.HasValue)
        {
            events = events.Where(evt => evt.ActorUserId == query.ActorUserId.Value);
        }

        if (query.From.HasValue)
        {
            events = events.Where(evt => evt.CreatedAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            events = events.Where(evt => evt.CreatedAt <= query.To.Value);
        }

        // Filter/order/limit in the database, then map the (≤ Take) rows in memory — enum-to-string
        // conversion happens client-side, where it needs no query translation.
        var rows = await events
            .Join(
                dbContext.Drafts.AsNoTracking(),
                evt => evt.DraftId,
                draft => draft.Id,
                (evt, draft) => new { Event = evt, draft.Name, draft.Code })
            .OrderByDescending(joined => joined.Event.CreatedAt)
            .ThenByDescending(joined => joined.Event.Sequence)
            .Take(query.Take)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(joined => new DraftEventRecord(
                joined.Event.DraftId,
                joined.Name,
                joined.Code,
                joined.Event.Sequence,
                joined.Event.Type.ToString(),
                joined.Event.FromStatus?.ToString(),
                joined.Event.ToStatus?.ToString(),
                joined.Event.Version,
                joined.Event.ActorUserId,
                joined.Event.Reason,
                joined.Event.CreatedAt))
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByTypeAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var events = dbContext.DraftEvents.AsNoTracking();

        if (from.HasValue)
        {
            events = events.Where(evt => evt.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            events = events.Where(evt => evt.CreatedAt <= to.Value);
        }

        // Group/count in the database; the enum-to-string projection happens after materialization.
        var grouped = await events
            .GroupBy(evt => evt.Type)
            .Select(group => new { Type = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);

        return grouped.ToDictionary(row => row.Type.ToString(), row => row.Count);
    }
}
