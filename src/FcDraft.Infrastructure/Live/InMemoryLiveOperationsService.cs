using System.Collections.Concurrent;
using System.Threading.Channels;
using FcDraft.Application.Common.Interfaces;

namespace FcDraft.Infrastructure.Live;

public sealed class InMemoryAdminNotificationService : IAdminNotificationService
{
    private readonly ConcurrentQueue<AdminNotification> _notifications = new();
    private readonly ConcurrentDictionary<Guid, Channel<AdminNotification>> _subscribers = new();

    public IReadOnlyCollection<AdminNotification> Recent() =>
        _notifications.Reverse().Take(30).ToArray();

    public void Publish(string type, string title, string message)
    {
        var notification = new AdminNotification(Guid.NewGuid(), type, title, message, DateTimeOffset.UtcNow);
        _notifications.Enqueue(notification);
        while (_notifications.Count > 50) _notifications.TryDequeue(out _);
        foreach (var channel in _subscribers.Values) channel.Writer.TryWrite(notification);
    }

    public async IAsyncEnumerable<AdminNotification> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subscriberId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<AdminNotification>();
        _subscribers[subscriberId] = channel;
        try
        {
            await foreach (var notification in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return notification;
            }
        }
        finally
        {
            _subscribers.TryRemove(subscriberId, out _);
        }
    }
}
