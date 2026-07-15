namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Runs a unit of work inside a single database transaction so multi-write operations either
/// commit together or leave no partial state. Provides the foundation the durable draft
/// aggregate (PR-10) will build audited command handlers on.
/// </summary>
public interface ITransactionRunner
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}
