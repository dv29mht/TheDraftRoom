using FcDraft.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>Reads outbox delivery metadata for the admin observability view. Never returns secrets.</summary>
public sealed class EfEmailOutboxReader(FcDraftDbContext dbContext) : IEmailOutboxReader
{
    public async Task<IReadOnlyList<EmailOutboxStatusView>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        var rows = await dbContext.EmailOutbox
            .AsNoTracking()
            .OrderByDescending(message => message.CreatedAt)
            .Take(count)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(message => new EmailOutboxStatusView(
                message.Id,
                message.Kind.ToString(),
                message.ToEmail,
                message.Status.ToString(),
                message.AttemptCount,
                message.LastError,
                message.CreatedAt,
                message.SentAt,
                message.NextAttemptAt,
                message.CampaignId))
            .ToArray();
    }

    public async Task<IReadOnlyList<CampaignDeliverySummary>> GetCampaignDeliveryAsync(
        IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken)
    {
        if (campaignIds.Count == 0)
        {
            return [];
        }

        var tallies = await dbContext.EmailOutbox
            .AsNoTracking()
            .Where(message => message.CampaignId.HasValue && campaignIds.Contains(message.CampaignId.Value))
            .GroupBy(message => new { message.CampaignId, message.Status })
            .Select(group => new { group.Key.CampaignId, group.Key.Status, Count = group.Count() })
            .ToArrayAsync(cancellationToken);

        return tallies
            .GroupBy(tally => tally.CampaignId!.Value)
            .Select(group => new CampaignDeliverySummary(
                group.Key,
                group.Where(tally => tally.Status == Domain.Entities.EmailOutboxStatus.Pending).Sum(tally => tally.Count),
                group.Where(tally => tally.Status == Domain.Entities.EmailOutboxStatus.Sent).Sum(tally => tally.Count),
                group.Where(tally => tally.Status == Domain.Entities.EmailOutboxStatus.Failed).Sum(tally => tally.Count)))
            .ToArray();
    }
}
