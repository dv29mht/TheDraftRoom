using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// The database-backed per-user notification store (PR-20). It shares the scoped
/// <see cref="FcDraftDbContext"/> with the draft command handlers, so rows added inside an
/// <c>ITransactionRunner</c> scope commit — or roll back — with the mutation that caused them.
/// </summary>
public sealed class EfUserNotificationStore(FcDraftDbContext dbContext) : IUserNotificationStore
{
    public Task AddAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        dbContext.UserNotifications.Add(notification);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<UserNotification>> ListAsync(
        Guid userId, bool unreadOnly, int take, CancellationToken cancellationToken) =>
        await dbContext.UserNotifications.AsNoTracking()
            .Where(notification => notification.UserId == userId)
            .Where(notification => !unreadOnly || notification.ReadAt == null)
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(Math.Clamp(take, 1, 200))
            .ToArrayAsync(cancellationToken);

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserNotifications
            .CountAsync(notification => notification.UserId == userId && notification.ReadAt == null, cancellationToken);

    public Task<UserNotification?> FindAsync(Guid notificationId, CancellationToken cancellationToken) =>
        dbContext.UserNotifications
            .FirstOrDefaultAsync(notification => notification.Id == notificationId, cancellationToken);

    public async Task<IReadOnlyList<UserNotification>> ListUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.UserNotifications
            .Where(notification => notification.UserId == userId && notification.ReadAt == null)
            .ToArrayAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
