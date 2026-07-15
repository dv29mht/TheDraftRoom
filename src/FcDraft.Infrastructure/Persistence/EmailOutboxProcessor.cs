using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Delivers due outbox rows via Brevo. On success the row is marked sent and its secret cleared; on
/// failure the attempt count grows and the next attempt is pushed out with exponential backoff until
/// <see cref="EmailOutboxMessage.MaxAttempts"/> is reached, after which the row is marked failed. The
/// stored error is the exception message only — never the secret.
/// </summary>
public sealed class EmailOutboxProcessor(
    FcDraftDbContext dbContext,
    IInvitationEmailSender invitationEmailSender,
    IPasswordResetEmailSender passwordResetEmailSender,
    ILogger<EmailOutboxProcessor> logger) : IEmailOutboxProcessor
{
    private const int BatchSize = 20;

    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await dbContext.EmailOutbox
            .Where(message => message.Status == EmailOutboxStatus.Pending && message.NextAttemptAt <= now)
            .OrderBy(message => message.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in due)
        {
            await DeliverAsync(message, cancellationToken);
        }

        if (due.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    private async Task DeliverAsync(EmailOutboxMessage message, CancellationToken cancellationToken)
    {
        message.AttemptCount++;
        try
        {
            var secret = message.Secret ?? string.Empty;
            Task send = message.Kind switch
            {
                EmailKind.Invitation => invitationEmailSender.SendAsync(message.ToEmail, message.ToName, secret, cancellationToken),
                EmailKind.PasswordReset => passwordResetEmailSender.SendAsync(message.ToEmail, message.ToName, secret, cancellationToken),
                _ => throw new InvalidOperationException($"Unknown email kind {message.Kind}."),
            };
            await send;

            message.Status = EmailOutboxStatus.Sent;
            message.SentAt = DateTimeOffset.UtcNow;
            message.LastError = null;
            message.Secret = null; // No longer needed; do not retain the secret past delivery.
        }
        catch (Exception exception)
        {
            message.LastError = exception.Message;
            if (message.AttemptCount >= message.MaxAttempts)
            {
                message.Status = EmailOutboxStatus.Failed;
                logger.LogError(exception, "Email {MessageId} failed permanently after {Attempts} attempts.",
                    message.Id, message.AttemptCount);
            }
            else
            {
                // Exponential backoff: 2^attempt minutes, capped, so a transient Brevo outage recovers.
                var delayMinutes = Math.Min(Math.Pow(2, message.AttemptCount), 60);
                message.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(delayMinutes);
                logger.LogWarning(exception, "Email {MessageId} delivery attempt {Attempts} failed; will retry.",
                    message.Id, message.AttemptCount);
            }
        }
    }
}
