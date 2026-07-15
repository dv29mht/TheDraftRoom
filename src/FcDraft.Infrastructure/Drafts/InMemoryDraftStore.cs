using System.Collections.Concurrent;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;

namespace FcDraft.Infrastructure.Drafts;

/// <summary>
/// In-memory draft aggregate store for the no-database foundation (and the hermetic in-memory tests).
/// Holds drafts per process; <see cref="FindAsync"/> returns the live aggregate that command handlers
/// mutate in place, so <see cref="SaveChangesAsync"/> is a no-op. Command handlers validate before they
/// mutate, so a rejected transition leaves the store unchanged. The transactional-rollback and
/// optimistic-concurrency guarantees under real constraints are proven by the PostgreSQL tests.
/// </summary>
public sealed class InMemoryDraftStore : IDraftStore
{
    private readonly ConcurrentDictionary<Guid, Draft> _drafts = new();

    public Task AddAsync(Draft draft, CancellationToken cancellationToken)
    {
        _drafts[draft.Id] = draft;
        return Task.CompletedTask;
    }

    public Task<Draft?> FindAsync(Guid draftId, CancellationToken cancellationToken) =>
        Task.FromResult(_drafts.TryGetValue(draftId, out var draft) ? draft : null);

    public Task<IReadOnlyList<Draft>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Draft>>(
            _drafts.Values.OrderByDescending(draft => draft.CreatedAt).ToArray());

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The <see cref="ITransactionRunner"/> for the in-memory foundation: it simply runs the operation, since
/// there is no database transaction to open. Registered only in the no-database branch — the SQL branch
/// uses <c>EfTransactionRunner</c>. This exists so the draft command handlers resolve a transaction runner
/// in both configurations.
/// </summary>
public sealed class InMemoryTransactionRunner : ITransactionRunner
{
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        operation(cancellationToken);

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        operation(cancellationToken);
}
