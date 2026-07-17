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

    /// <summary>
    /// Enqueues one §9.8 draft-lifecycle email (PR-20). In the persistent configuration this writes an
    /// outbox row inside the caller's draft transaction; in the in-memory foundation delivery failures are
    /// swallowed (logged) — either way, a Brevo outage can never roll back the draft mutation.
    /// </summary>
    Task EnqueueDraftEmailAsync(
        EmailKind kind, string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues one §9.8 announcement email (PR-21). In the persistent configuration this writes an
    /// outbox row (stamped with the campaign id) inside the caller's transaction, deliverable no earlier
    /// than <paramref name="notBefore"/> — the throttle: the send command staggers a bulk audience across
    /// delivery windows instead of releasing every email at once. The in-memory foundation has no worker
    /// to defer to, so it delivers immediately and swallows failures like the draft emails.
    /// </summary>
    Task EnqueueAnnouncementAsync(
        string email, string displayName, AnnouncementEmailPayload payload, DateTimeOffset notBefore,
        CancellationToken cancellationToken);
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

    /// <summary>
    /// Per-campaign delivery tallies (PR-21, §9.8 delivery visibility): how many of each campaign's
    /// announcement emails are still pending, delivered, or permanently failed.
    /// </summary>
    Task<IReadOnlyList<CampaignDeliverySummary>> GetCampaignDeliveryAsync(
        IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken);

    /// <summary>
    /// Delivery tallies across ALL outbox messages (PR-24 admin Overview health): still pending,
    /// delivered, or permanently failed. On the in-memory branch every inline delivery is already
    /// Sent or Failed (never Pending).
    /// </summary>
    Task<EmailDeliveryTallies> GetStatusTalliesAsync(CancellationToken cancellationToken);
}

/// <summary>Delivery tallies across the whole outbox: pending vs sent vs permanently failed.</summary>
public sealed record EmailDeliveryTallies(int Pending, int Sent, int Failed);

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
    DateTimeOffset NextAttemptAt,
    Guid? CampaignId = null);

/// <summary>Delivery tallies for one announcement campaign's outbox emails.</summary>
public sealed record CampaignDeliverySummary(Guid CampaignId, int Pending, int Sent, int Failed);
