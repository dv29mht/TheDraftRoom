using FcDraft.Domain.Entities;

namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Persists the draft aggregate (PR-10). <see cref="FindAsync"/> returns the full aggregate (participants,
/// teams and their members, snapshotted slots, and event history) so a command can validate and mutate it,
/// then <see cref="SaveChangesAsync"/> commits. Backed by the database when persistence is configured; the
/// in-memory foundation holds drafts per process. Command handlers wrap Find/mutate/Save in
/// <see cref="ITransactionRunner"/> so a failed transition leaves no partial write.
/// </summary>
public interface IDraftStore
{
    Task AddAsync(Draft draft, CancellationToken cancellationToken);

    Task<Draft?> FindAsync(Guid draftId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Draft>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The ids of drafts whose active position turn has run past its deadline at <paramref name="now"/>
    /// (unpaused, clock anchored). Feeds the PR-16 hosted expiry sweep; the read-path lazy evaluation
    /// remains the authority when the instance was cold.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDraftIdsWithExpiredTurnsAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
