using FcDraft.Domain.Entities;

namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Enqueues transactional emails. In the persistent configuration this writes to the durable outbox
/// so the caller's account transaction commits regardless of Brevo availability; in the in-memory
/// foundation it delivers directly. Callers (account creation, reset requests) depend only on this
/// seam, not on how or when delivery happens.
/// </summary>
public interface IEmailQueue
{
    Task EnqueueInvitationAsync(string email, string displayName, string temporaryPassword, CancellationToken cancellationToken);
    Task EnqueuePasswordResetAsync(string email, string displayName, string resetToken, CancellationToken cancellationToken);
}

/// <summary>
/// Delivers due outbox messages via Brevo, applying retry/backoff. Exposed so the background worker
/// and tests can drive delivery through the same code path.
/// </summary>
public interface IEmailOutboxProcessor
{
    /// <summary>Attempts delivery of every currently-due message; returns how many were processed.</summary>
    Task<int> ProcessDueAsync(CancellationToken cancellationToken);
}

/// <summary>Read-only view of outbox delivery status for admin observability. Never exposes secrets.</summary>
public interface IEmailOutboxReader
{
    Task<IReadOnlyList<EmailOutboxStatusView>> GetRecentAsync(int count, CancellationToken cancellationToken);
}

/// <summary>Delivery metadata for one outbox message, with no secret payload.</summary>
public sealed record EmailOutboxStatusView(
    Guid Id,
    string Kind,
    string ToEmail,
    string Status,
    int AttemptCount,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset NextAttemptAt);
