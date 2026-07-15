using FcDraft.Application.Common.Interfaces;

namespace FcDraft.Infrastructure.Email;

/// <summary>
/// The in-memory foundation's email queue: delivers immediately through the Brevo senders. A send
/// failure propagates so account creation rolls back exactly as it did before the outbox existed —
/// there is no database to make delivery durable, so this preserves the original semantics.
/// </summary>
public sealed class DirectEmailQueue(
    IInvitationEmailSender invitationEmailSender,
    IPasswordResetEmailSender passwordResetEmailSender) : IEmailQueue
{
    public Task EnqueueInvitationAsync(string email, string displayName, string temporaryPassword, CancellationToken cancellationToken) =>
        invitationEmailSender.SendAsync(email, displayName, temporaryPassword, cancellationToken);

    public Task EnqueuePasswordResetAsync(string email, string displayName, string resetToken, CancellationToken cancellationToken) =>
        passwordResetEmailSender.SendAsync(email, displayName, resetToken, cancellationToken);
}
