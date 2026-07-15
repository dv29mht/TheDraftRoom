using FcDraft.Domain.Entities;

namespace FcDraft.Application.Common.Interfaces;

public sealed record AdminNotification(
    Guid Id,
    string Type,
    string Title,
    string Message,
    DateTimeOffset CreatedAt);

public interface IAdminNotificationService
{
    IReadOnlyCollection<AdminNotification> Recent();
    void Publish(string type, string title, string message);
    IAsyncEnumerable<AdminNotification> SubscribeAsync(CancellationToken cancellationToken);
}

public sealed record DraftRoom(
    Guid Id,
    string Code,
    string Name,
    string Format,
    Guid HostUserId,
    string HostName,
    DateTimeOffset CreatedAt);

public interface IDraftRoomService
{
    IReadOnlyCollection<DraftRoom> List();
    DraftRoom Create(string name, string format, User host);
}
