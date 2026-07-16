using System.Collections.Concurrent;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;

namespace FcDraft.Infrastructure.Notifications;

/// <summary>
/// The in-memory foundation's per-user notification store (PR-20), used by the hermetic test suite. No
/// real transactions exist here (the in-memory <c>ITransactionRunner</c> is pass-through), so adds are
/// visible immediately and <see cref="SaveChangesAsync"/> is a no-op — the SQL branch supplies the
/// commit/rollback semantics.
/// </summary>
public sealed class InMemoryUserNotificationStore : IUserNotificationStore
{
    private readonly ConcurrentDictionary<Guid, UserNotification> _notifications = new();

    public Task AddAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        _notifications[notification.Id] = notification;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserNotification>> ListAsync(
        Guid userId, bool unreadOnly, int take, CancellationToken cancellationToken)
    {
        IReadOnlyList<UserNotification> items = _notifications.Values
            .Where(notification => notification.UserId == userId)
            .Where(notification => !unreadOnly || notification.ReadAt is null)
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(Math.Clamp(take, 1, 200))
            .ToArray();
        return Task.FromResult(items);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_notifications.Values.Count(notification => notification.UserId == userId && notification.ReadAt is null));

    public Task<UserNotification?> FindAsync(Guid notificationId, CancellationToken cancellationToken) =>
        Task.FromResult(_notifications.TryGetValue(notificationId, out var notification) ? notification : null);

    public Task<IReadOnlyList<UserNotification>> ListUnreadAsync(Guid userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<UserNotification> items = _notifications.Values
            .Where(notification => notification.UserId == userId && notification.ReadAt is null)
            .ToArray();
        return Task.FromResult(items);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
