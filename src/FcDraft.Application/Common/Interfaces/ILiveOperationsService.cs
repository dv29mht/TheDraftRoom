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
