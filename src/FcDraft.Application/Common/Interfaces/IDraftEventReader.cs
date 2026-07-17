using FcDraft.Domain.Entities;

namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Read-only queries over the append-only draft event trail, across ALL drafts (PR-21, §9.10) — the
/// admin audit view. Deliberately exposes no mutation: draft events are written only by the aggregate
/// (one immutable row per accepted transition) and are never edited or deleted; recovery appends
/// compensating events instead.
/// </summary>
public interface IDraftEventReader
{
    /// <summary>Matching events, newest first, capped at <see cref="DraftEventQuery.Take"/>.</summary>
    Task<IReadOnlyList<DraftEventRecord>> QueryAsync(DraftEventQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Counts events by type across ALL drafts within the optional window (event-type name → count).
    /// Powers the §15-equivalent engagement figures on the admin Overview (PR-24) without reading the
    /// write-only metrics meter. Types with no events are simply absent from the dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> CountByTypeAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
}

/// <summary>Audit filters (§17.8: draft, user, type, date). Null members match everything.</summary>
public sealed record DraftEventQuery(
    Guid? DraftId,
    DraftEventType? Type,
    Guid? ActorUserId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Take);

/// <summary>One immutable draft event joined with its draft's name/code for display.</summary>
public sealed record DraftEventRecord(
    Guid DraftId,
    string DraftName,
    string DraftCode,
    int Sequence,
    string Type,
    string? FromStatus,
    string? ToStatus,
    int Version,
    Guid? ActorUserId,
    string? Reason,
    DateTimeOffset CreatedAt);
