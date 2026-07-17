using System.Collections.Concurrent;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;

namespace FcDraft.Infrastructure.Auth;

/// <summary>
/// Keeps the most recent security-audit events for the running process when no database is
/// configured. Bounded so it never grows without limit; durable persistence is used whenever SQL
/// persistence is enabled (see <see cref="Persistence.EfSecurityAuditService"/>).
/// </summary>
public sealed class InMemorySecurityAuditService : ISecurityAuditService
{
    private const int Capacity = 500;
    private readonly ConcurrentQueue<SecurityAuditEvent> _events = new();

    public Task RecordAsync(SecurityAuditEntry entry, CancellationToken cancellationToken)
    {
        _events.Enqueue(new SecurityAuditEvent
        {
            UserId = entry.UserId,
            Email = entry.Email,
            Action = entry.Action,
            Detail = entry.Detail,
            IpAddress = entry.IpAddress,
        });

        while (_events.Count > Capacity && _events.TryDequeue(out _))
        {
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SecurityAuditEvent>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        IReadOnlyList<SecurityAuditEvent> recent = _events
            .Reverse()
            .Take(count)
            .ToArray();
        return Task.FromResult(recent);
    }

    public Task<IReadOnlyList<SecurityAuditEvent>> QueryAsync(
        SecurityAuditQuery query, CancellationToken cancellationToken)
    {
        var needle = query.Email?.Trim();
        IReadOnlyList<SecurityAuditEvent> matches = _events
            .Reverse()
            .Where(audit => !query.Action.HasValue || audit.Action == query.Action.Value)
            .Where(audit => !query.UserId.HasValue || audit.UserId == query.UserId.Value)
            .Where(audit => string.IsNullOrWhiteSpace(needle)
                || (audit.Email is not null && audit.Email.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .Where(audit => !query.From.HasValue || audit.CreatedAt >= query.From.Value)
            .Where(audit => !query.To.HasValue || audit.CreatedAt <= query.To.Value)
            .Take(query.Take)
            .ToArray();
        return Task.FromResult(matches);
    }
}
