using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// The durable email queue: writes a pending outbox row and commits it. Enqueuing never contacts
/// Brevo, so it cannot fail because of a mail outage — the caller's account transaction is safe. A
/// background worker delivers the row afterward.
/// </summary>
public sealed class OutboxEmailQueue(FcDraftDbContext dbContext) : IEmailQueue
{
    public Task EnqueueInvitationAsync(string email, string displayName, string temporaryPassword, CancellationToken cancellationToken) =>
        EnqueueAsync(EmailKind.Invitation, email, displayName, temporaryPassword, cancellationToken);

    public Task EnqueuePasswordResetAsync(string email, string displayName, string resetToken, CancellationToken cancellationToken) =>
        EnqueueAsync(EmailKind.PasswordReset, email, displayName, resetToken, cancellationToken);

    /// <summary>
    /// Draft-lifecycle emails (PR-20): the row commits inside the caller's draft transaction (the same
    /// scoped DbContext), so the mutation and its email are atomic — and Brevo is never touched inline.
    /// </summary>
    public async Task EnqueueDraftEmailAsync(
        EmailKind kind, string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken)
    {
        dbContext.EmailOutbox.Add(new EmailOutboxMessage
        {
            Kind = kind,
            ToEmail = email,
            ToName = displayName,
            Payload = System.Text.Json.JsonSerializer.Serialize(payload),
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnqueueAsync(
        EmailKind kind,
        string email,
        string displayName,
        string secret,
        CancellationToken cancellationToken)
    {
        dbContext.EmailOutbox.Add(new EmailOutboxMessage
        {
            Kind = kind,
            ToEmail = email,
            ToName = displayName,
            Secret = secret,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
