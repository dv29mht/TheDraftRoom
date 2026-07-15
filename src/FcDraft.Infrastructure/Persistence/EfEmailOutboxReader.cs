using FcDraft.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>Reads outbox delivery metadata for the admin observability view. Never returns secrets.</summary>
public sealed class EfEmailOutboxReader(FcDraftDbContext dbContext) : IEmailOutboxReader
{
    public async Task<IReadOnlyList<EmailOutboxStatusView>> GetRecentAsync(int count, CancellationToken cancellationToken) =>
        await dbContext.EmailOutbox
            .AsNoTracking()
            .OrderByDescending(message => message.CreatedAt)
            .Take(count)
            .Select(message => new EmailOutboxStatusView(
                message.Id,
                message.Kind.ToString(),
                message.ToEmail,
                message.Status.ToString(),
                message.AttemptCount,
                message.LastError,
                message.CreatedAt,
                message.SentAt,
                message.NextAttemptAt))
            .ToArrayAsync(cancellationToken);
}
