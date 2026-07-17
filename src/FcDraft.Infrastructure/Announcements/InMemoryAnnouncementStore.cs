using System.Collections.Concurrent;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;

namespace FcDraft.Infrastructure.Announcements;

/// <summary>
/// Holds announcement campaign records per process when no database is configured. Append-only like
/// its EF counterpart: nothing here (or anywhere) updates or deletes a record. SaveChanges is a no-op
/// because <see cref="AddAsync"/> is immediately visible, matching the in-memory transaction runner.
/// </summary>
public sealed class InMemoryAnnouncementStore : IAnnouncementStore
{
    private readonly ConcurrentDictionary<Guid, Announcement> _announcements = new();

    public Task AddAsync(Announcement announcement, CancellationToken cancellationToken)
    {
        _announcements[announcement.Id] = announcement;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Announcement>> ListRecentAsync(int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Announcement>>(
            _announcements.Values
                .OrderByDescending(announcement => announcement.RequestedAt)
                .ThenByDescending(announcement => announcement.Id)
                .Take(count)
                .ToArray());

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
