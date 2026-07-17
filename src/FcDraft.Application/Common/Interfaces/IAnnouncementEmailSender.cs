namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Non-secret template payload for one announcement email (PR-21, §9.8). <see cref="CampaignId"/> is
/// the <see cref="Domain.Entities.Announcement"/> record's id — carried to Brevo as campaign metadata
/// and stamped on the outbox row for delivery visibility. <see cref="DraftId"/>/<see cref="DraftName"/>
/// are set for the draft-participants audience so the email can deep-link to the draft.
/// </summary>
public sealed record AnnouncementEmailPayload(
    Guid CampaignId, string Subject, string Body, Guid? DraftId = null, string? DraftName = null);

/// <summary>
/// Sends one §9.8 announcement email through Brevo. Only the outbox processor (or the in-memory
/// direct queue) calls this — the announcement command enqueues via <see cref="IEmailQueue"/> and never
/// touches Brevo inline, so a mail outage can never roll back the announcement record.
/// </summary>
public interface IAnnouncementEmailSender
{
    Task SendAsync(string email, string displayName, AnnouncementEmailPayload payload, CancellationToken cancellationToken);
}
