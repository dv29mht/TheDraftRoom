using FcDraft.Application.Common.Interfaces;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Runs an operation inside a single EF Core transaction, committing on success and rolling back
/// on any exception so a failed multi-write leaves no partial state.
/// </summary>
public sealed class EfTransactionRunner(FcDraftDbContext dbContext) : ITransactionRunner
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        ExecuteAsync<object?>(async ct =>
        {
            await operation(ct);
            return null;
        }, cancellationToken);
}
