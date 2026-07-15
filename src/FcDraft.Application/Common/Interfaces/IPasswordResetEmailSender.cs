namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Sends the transactional password-reset email through Brevo. Mirrors
/// <see cref="IInvitationEmailSender"/>; PR-06 routes both through the durable outbox. Tests supply
/// a fake so no real email is sent and the emailed token can be captured.
/// </summary>
public interface IPasswordResetEmailSender
{
    Task SendAsync(string email, string displayName, string resetToken, CancellationToken cancellationToken);
}
