using FcDraft.Application.Common.Interfaces;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Notifications;

/// <summary>One per-user notification for the centre (PR-20, §9.9); deep-links to /drafts/{DraftId}.</summary>
public sealed record UserNotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    Guid? DraftId,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt);

public sealed record UserNotificationsDto(IReadOnlyList<UserNotificationDto> Items, int UnreadCount);

/// <summary>The caller's notifications, newest first, plus the unread badge count.</summary>
public sealed record ListMyNotificationsQuery(Guid UserId, bool UnreadOnly = false, int Take = 50)
    : IRequest<UserNotificationsDto>;

/// <summary>Marks ONE of the caller's notifications read. Someone else's id reads as not-found (404).</summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId, Guid UserId) : IRequest<UserNotificationsDto>;

/// <summary>Marks every unread notification of the caller read.</summary>
public sealed record MarkAllNotificationsReadCommand(Guid UserId) : IRequest<UserNotificationsDto>;

public sealed class ListMyNotificationsQueryValidator : AbstractValidator<ListMyNotificationsQuery>
{
    public ListMyNotificationsQueryValidator()
    {
        RuleFor(query => query.UserId).NotEmpty();
        RuleFor(query => query.Take).InclusiveBetween(1, 200);
    }
}

public sealed class ListMyNotificationsQueryHandler(IUserNotificationStore store)
    : IRequestHandler<ListMyNotificationsQuery, UserNotificationsDto>
{
    public async Task<UserNotificationsDto> Handle(ListMyNotificationsQuery request, CancellationToken cancellationToken) =>
        await ProjectAsync(store, request.UserId, request.UnreadOnly, request.Take, cancellationToken);

    internal static async Task<UserNotificationsDto> ProjectAsync(
        IUserNotificationStore store, Guid userId, bool unreadOnly, int take, CancellationToken cancellationToken)
    {
        var items = await store.ListAsync(userId, unreadOnly, take, cancellationToken);
        var unread = await store.CountUnreadAsync(userId, cancellationToken);
        return new UserNotificationsDto(
            items.Select(notification => new UserNotificationDto(
                notification.Id, notification.Type, notification.Title, notification.Body,
                notification.DraftId, notification.ReadAt, notification.CreatedAt)).ToArray(),
            unread);
    }
}

public sealed class MarkNotificationReadCommandHandler(
    IUserNotificationStore store, ITransactionRunner transaction, TimeProvider clock)
    : IRequestHandler<MarkNotificationReadCommand, UserNotificationsDto>
{
    public async Task<UserNotificationsDto> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        await transaction.ExecuteAsync(async ct =>
        {
            var notification = await store.FindAsync(request.NotificationId, ct);
            // Authorization-scoped: another user's notification is indistinguishable from a missing one.
            if (notification is null || notification.UserId != request.UserId)
            {
                throw new KeyNotFoundException("Notification not found.");
            }

            if (notification.ReadAt is null)
            {
                notification.ReadAt = clock.GetUtcNow();
                await store.SaveChangesAsync(ct);
            }
        }, cancellationToken);

        return await ListMyNotificationsQueryHandler.ProjectAsync(store, request.UserId, false, 50, cancellationToken);
    }
}

public sealed class MarkAllNotificationsReadCommandHandler(
    IUserNotificationStore store, ITransactionRunner transaction, TimeProvider clock)
    : IRequestHandler<MarkAllNotificationsReadCommand, UserNotificationsDto>
{
    public async Task<UserNotificationsDto> Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        await transaction.ExecuteAsync(async ct =>
        {
            var unread = await store.ListUnreadAsync(request.UserId, ct);
            if (unread.Count == 0)
            {
                return;
            }

            var now = clock.GetUtcNow();
            foreach (var notification in unread)
            {
                notification.ReadAt = now;
            }

            await store.SaveChangesAsync(ct);
        }, cancellationToken);

        return await ListMyNotificationsQueryHandler.ProjectAsync(store, request.UserId, false, 50, cancellationToken);
    }
}
