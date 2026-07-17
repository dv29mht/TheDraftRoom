using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FcDraft.Infrastructure.Email;

/// <summary>
/// The in-memory foundation's email queue: delivers immediately through the Brevo senders. An ACCOUNT
/// send failure propagates so account creation rolls back exactly as it did before the outbox existed.
/// A DRAFT-lifecycle or ANNOUNCEMENT send failure (PR-20/PR-21) is swallowed and logged instead — a
/// mail outage must never fail or roll back the mutation, matching the durable outbox's guarantee on
/// the SQL branch. Every outcome is recorded in <see cref="InMemoryEmailOutbox"/> so the admin
/// delivery-visibility endpoints work on this branch too.
/// </summary>
public sealed class DirectEmailQueue(
    IInvitationEmailSender invitationEmailSender,
    IPasswordResetEmailSender passwordResetEmailSender,
    IDraftEmailSender draftEmailSender,
    IAnnouncementEmailSender announcementEmailSender,
    InMemoryEmailOutbox outbox,
    ILogger<DirectEmailQueue> logger) : IEmailQueue
{
    public Task EnqueueInvitationAsync(string email, string displayName, string temporaryPassword, CancellationToken cancellationToken) =>
        SendAccountEmailAsync(
            EmailKind.Invitation, email,
            invitationEmailSender.SendAsync(email, displayName, temporaryPassword, cancellationToken));

    public Task EnqueuePasswordResetAsync(string email, string displayName, string resetToken, CancellationToken cancellationToken) =>
        SendAccountEmailAsync(
            EmailKind.PasswordReset, email,
            passwordResetEmailSender.SendAsync(email, displayName, resetToken, cancellationToken));

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
            outbox.Record(kind, email, campaignId: null, delivered: true, error: null);
        }
        catch (Exception exception)
        {
            outbox.Record(kind, email, campaignId: null, delivered: false, error: exception.Message);
            logger.LogWarning(exception,
                "Draft email {Kind} to {Email} failed; the draft mutation proceeds (no durable outbox in-memory).",
                kind, email);
        }
    }

    /// <summary>
    /// Announcements (PR-21): no worker exists in-memory, so <paramref name="notBefore"/> cannot defer
    /// anything — delivery is immediate and the throttle is a durable-outbox concern. Failures are
    /// swallowed exactly like the draft emails: an outage never rolls back the announcement.
    /// </summary>
    public async Task EnqueueAnnouncementAsync(
        string email, string displayName, AnnouncementEmailPayload payload, DateTimeOffset notBefore,
        CancellationToken cancellationToken)
    {
        try
        {
            await announcementEmailSender.SendAsync(email, displayName, payload, cancellationToken);
            outbox.Record(EmailKind.Announcement, email, payload.CampaignId, delivered: true, error: null);
        }
        catch (Exception exception)
        {
            outbox.Record(EmailKind.Announcement, email, payload.CampaignId, delivered: false, error: exception.Message);
            logger.LogWarning(exception,
                "Announcement email to {Email} failed; the announcement proceeds (no durable outbox in-memory).",
                email);
        }
    }

    /// <summary>Account emails keep their original contract: a failure propagates to the caller.</summary>
    private async Task SendAccountEmailAsync(EmailKind kind, string email, Task send)
    {
        try
        {
            await send;
            outbox.Record(kind, email, campaignId: null, delivered: true, error: null);
        }
        catch (Exception exception)
        {
            outbox.Record(kind, email, campaignId: null, delivered: false, error: exception.Message);
            throw;
        }
    }
}
