using System.Collections.Concurrent;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;

namespace FcDraft.Infrastructure.Email;

/// <summary>
/// Delivery ledger for the in-memory foundation, where email is delivered inline and there is no
/// durable outbox table. <see cref="DirectEmailQueue"/> records every send outcome here, so the admin
/// delivery-visibility endpoints (recent outbox + per-campaign tallies, PR-21 §9.8) work uniformly in
/// both configurations — an in-memory row is simply already Sent or Failed, never Pending. Bounded so
/// it never grows without limit; durable persistence is used whenever SQL persistence is enabled.
/// </summary>
public sealed class InMemoryEmailOutbox(TimeProvider clock, IProductAnalytics? analytics = null) : IEmailOutboxReader
{
    private const int Capacity = 1000;
    private readonly ConcurrentQueue<EmailOutboxStatusView> _entries = new();

    /// <summary>Records one inline delivery outcome (never a secret — metadata only).</summary>
    public void Record(EmailKind kind, string toEmail, Guid? campaignId, bool delivered, string? error)
    {
        // §15 delivery rate on the inline branch (the SQL branch samples in EmailOutboxProcessor).
        // Inline delivery has no retry, so the outcome is final either way.
        (analytics ?? NullProductAnalytics.Instance).EmailDelivery(delivered ? "sent" : "failed");

        var now = clock.GetUtcNow();
        _entries.Enqueue(new EmailOutboxStatusView(
            Guid.NewGuid(),
            kind.ToString(),
            toEmail,
            delivered ? EmailOutboxStatus.Sent.ToString() : EmailOutboxStatus.Failed.ToString(),
            AttemptCount: 1,
            LastError: error,
            CreatedAt: now,
            SentAt: delivered ? now : null,
            NextAttemptAt: now,
            CampaignId: campaignId));

        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }
    }

    public Task<IReadOnlyList<EmailOutboxStatusView>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        IReadOnlyList<EmailOutboxStatusView> recent = _entries
            .Reverse()
            .Take(count)
            .ToArray();
        return Task.FromResult(recent);
    }

    public Task<EmailDeliveryTallies> GetStatusTalliesAsync(CancellationToken cancellationToken)
    {
        var entries = _entries.ToArray();
        int For(EmailOutboxStatus status) => entries.Count(entry => entry.Status == status.ToString());
        return Task.FromResult(new EmailDeliveryTallies(
            For(EmailOutboxStatus.Pending), // always 0 inline — kept for shape parity with the SQL branch
            For(EmailOutboxStatus.Sent),
            For(EmailOutboxStatus.Failed)));
    }

    public Task<IReadOnlyList<CampaignDeliverySummary>> GetCampaignDeliveryAsync(
        IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken)
    {
        IReadOnlyList<CampaignDeliverySummary> summaries = _entries
            .Where(entry => entry.CampaignId.HasValue && campaignIds.Contains(entry.CampaignId.Value))
            .GroupBy(entry => entry.CampaignId!.Value)
            .Select(group => new CampaignDeliverySummary(
                group.Key,
                Pending: 0, // Inline delivery leaves nothing pending.
                Sent: group.Count(entry => entry.Status == nameof(EmailOutboxStatus.Sent)),
                Failed: group.Count(entry => entry.Status == nameof(EmailOutboxStatus.Failed))))
            .ToArray();
        return Task.FromResult(summaries);
    }
}
