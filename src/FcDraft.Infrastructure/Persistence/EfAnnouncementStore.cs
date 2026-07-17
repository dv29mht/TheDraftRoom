using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Persists announcement campaign records. Shares the scoped <see cref="FcDraftDbContext"/>, so an
/// added record commits inside the announcement command's ambient transaction — the campaign, its
/// notifications, its outbox emails, and its audit record are atomic.
/// </summary>
public sealed class EfAnnouncementStore(FcDraftDbContext dbContext) : IAnnouncementStore
{
    public Task AddAsync(Announcement announcement, CancellationToken cancellationToken)
    {
        dbContext.Announcements.Add(announcement);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Announcement>> ListRecentAsync(int count, CancellationToken cancellationToken) =>
        await dbContext.Announcements
            .AsNoTracking()
            .OrderByDescending(announcement => announcement.RequestedAt)
            .ThenByDescending(announcement => announcement.Id)
            .Take(count)
            .ToArrayAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
