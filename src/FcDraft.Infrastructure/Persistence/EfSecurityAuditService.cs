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

    public async Task<IReadOnlyList<SecurityAuditEvent>> QueryAsync(
        SecurityAuditQuery query, CancellationToken cancellationToken)
    {
        var events = dbContext.SecurityAuditEvents.AsNoTracking();

        if (query.Action.HasValue)
        {
            events = events.Where(audit => audit.Action == query.Action.Value);
        }

        if (query.UserId.HasValue)
        {
            events = events.Where(audit => audit.UserId == query.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Email))
        {
            var needle = query.Email.Trim().ToLowerInvariant();
            events = events.Where(audit => audit.Email != null && audit.Email.ToLower().Contains(needle));
        }

        if (query.From.HasValue)
        {
            events = events.Where(audit => audit.CreatedAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            events = events.Where(audit => audit.CreatedAt <= query.To.Value);
        }

        return await events
            .OrderByDescending(audit => audit.CreatedAt)
            .ThenByDescending(audit => audit.Id)
            .Take(query.Take)
            .ToArrayAsync(cancellationToken);
    }
}
