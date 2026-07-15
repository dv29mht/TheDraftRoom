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
}
