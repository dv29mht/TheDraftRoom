using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>Persists append-only security-audit events to PostgreSQL.</summary>
public sealed class EfSecurityAuditService(FcDraftDbContext dbContext) : ISecurityAuditService
{
    public async Task RecordAsync(SecurityAuditEntry entry, CancellationToken cancellationToken)
    {
        dbContext.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            UserId = entry.UserId,
            Email = entry.Email,
            Action = entry.Action,
            Detail = entry.Detail,
            IpAddress = entry.IpAddress,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SecurityAuditEvent>> GetRecentAsync(int count, CancellationToken cancellationToken) =>
        await dbContext.SecurityAuditEvents
            .AsNoTracking()
            .OrderByDescending(audit => audit.CreatedAt)
            .ThenByDescending(audit => audit.Id)
            .Take(count)
            .ToArrayAsync(cancellationToken);
}
