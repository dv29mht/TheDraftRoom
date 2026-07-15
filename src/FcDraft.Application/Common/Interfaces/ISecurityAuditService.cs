using FcDraft.Domain.Entities;

namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Appends immutable security-audit events. Never receives passwords or tokens. Records survive in
/// the database when persistence is configured; otherwise a bounded in-memory ring keeps the most
/// recent events for the running process.
/// </summary>
public interface ISecurityAuditService
{
    Task RecordAsync(SecurityAuditEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<SecurityAuditEvent>> GetRecentAsync(int count, CancellationToken cancellationToken);
}

/// <summary>Input for a single audit record. IP/detail are optional and never sensitive.</summary>
public sealed record SecurityAuditEntry(
    SecurityAuditAction Action,
    Guid? UserId = null,
    string? Email = null,
    string? Detail = null,
    string? IpAddress = null);
