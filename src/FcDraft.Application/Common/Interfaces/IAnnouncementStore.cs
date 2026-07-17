using FcDraft.Domain.Entities;

namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// Persists the append-only announcement campaign records (PR-21, §9.8). Add-only by design: no
/// update or delete member exists, so no normal API path can edit the campaign trail. Backed by the
/// database when persistence is configured (added rows commit inside the caller's transaction); the
/// in-memory foundation holds them per process.
/// </summary>
public interface IAnnouncementStore
{
    Task AddAsync(Announcement announcement, CancellationToken cancellationToken);

    /// <summary>The most recent campaigns, newest first.</summary>
    Task<IReadOnlyList<Announcement>> ListRecentAsync(int count, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
