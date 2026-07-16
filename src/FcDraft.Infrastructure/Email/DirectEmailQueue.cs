using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FcDraft.Infrastructure.Email;

/// <summary>
/// The in-memory foundation's email queue: delivers immediately through the Brevo senders. An ACCOUNT
/// send failure propagates so account creation rolls back exactly as it did before the outbox existed.
/// A DRAFT-lifecycle send failure (PR-20) is swallowed and logged instead — a mail outage must never
/// fail or roll back a draft mutation, matching the durable outbox's guarantee on the SQL branch.
/// </summary>
public sealed class DirectEmailQueue(
    IInvitationEmailSender invitationEmailSender,
    IPasswordResetEmailSender passwordResetEmailSender,
    IDraftEmailSender draftEmailSender,
    ILogger<DirectEmailQueue> logger) : IEmailQueue
{
    public Task EnqueueInvitationAsync(string email, string displayName, string temporaryPassword, CancellationToken cancellationToken) =>
        invitationEmailSender.SendAsync(email, displayName, temporaryPassword, cancellationToken);

    public Task EnqueuePasswordResetAsync(string email, string displayName, string resetToken, CancellationToken cancellationToken) =>
        passwordResetEmailSender.SendAsync(email, displayName, resetToken, cancellationToken);

    public async Task EnqueueDraftEmailAsync(
        EmailKind kind, string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            Task send = kind switch
            {
                EmailKind.DraftInvitation => draftEmailSender.SendInvitationAsync(email, displayName, payload, cancellationToken),
                EmailKind.DraftReminder => draftEmailSender.SendReminderAsync(email, displayName, payload, cancellationToken),
                EmailKind.DraftCancelled => draftEmailSender.SendCancelledAsync(email, displayName, payload, cancellationToken),
                EmailKind.DraftCompleted => draftEmailSender.SendCompletedAsync(email, displayName, payload, cancellationToken),
                _ => throw new InvalidOperationException($"{kind} is not a draft email kind."),
            };
            await send;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Draft email {Kind} to {Email} failed; the draft mutation proceeds (no durable outbox in-memory).",
                kind, email);
        }
    }
}
