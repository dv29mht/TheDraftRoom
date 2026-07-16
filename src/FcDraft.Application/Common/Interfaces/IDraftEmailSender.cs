namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Non-secret template payload for one draft-lifecycle email (PR-20, §9.8): the draft to deep-link to,
/// its display name, and — for cancellations — the recorded reason.
/// </summary>
public sealed record DraftEmailPayload(Guid DraftId, string DraftName, string? Reason = null);

/// <summary>
/// Sends the four §9.8 draft-lifecycle templates through Brevo. Only the outbox processor (or the
/// in-memory direct queue) calls this — draft transactions enqueue via <see cref="IEmailQueue"/> and never
/// touch Brevo inline, so a mail outage can never roll back a draft mutation.
/// </summary>
public interface IDraftEmailSender
{
    Task SendInvitationAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken);
    Task SendReminderAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken);
    Task SendCancelledAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken);
    Task SendCompletedAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken);
}
