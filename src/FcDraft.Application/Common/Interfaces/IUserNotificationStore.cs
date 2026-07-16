using FcDraft.Domain.Entities;

namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// The persistent per-user notification store (PR-20, §9.9). Adds participate in the caller's ambient
/// transaction (the same <see cref="ITransactionRunner"/> scope as the draft mutation that caused them),
/// so a rolled-back command never notifies. Every read is scoped by user id — the API layer resolves the
/// id from the caller's token, never from input.
/// </summary>
public interface IUserNotificationStore
{
    Task AddAsync(UserNotification notification, CancellationToken cancellationToken);

    /// <summary>The user's notifications, newest first.</summary>
    Task<IReadOnlyList<UserNotification>> ListAsync(Guid userId, bool unreadOnly, int take, CancellationToken cancellationToken);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserNotification?> FindAsync(Guid notificationId, CancellationToken cancellationToken);

    /// <summary>The user's unread notifications (for mark-all-read), tracked for mutation.</summary>
    Task<IReadOnlyList<UserNotification>> ListUnreadAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
