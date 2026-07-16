using FcDraft.Application.Features.Drafts;

namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// One version-stamped live update for a draft (PR-17, PRD §17.7): who it is about, the version the change
/// produced, the event that caused it, and — when the producer had it — the authoritative
/// <see cref="DraftDetail"/> snapshot. <see cref="Detail"/> may be null (e.g. a command that only returns a
/// summary); a client that receives a null detail, or sees a gap between its version and
/// <see cref="Version"/>, refetches the authoritative snapshot over REST.
/// </summary>
public sealed record DraftUpdateNotification(Guid DraftId, int Version, string EventType, DraftDetail? Detail);

/// <summary>
/// The live-propagation seam (PR-17). Publishers call it AFTER a mutation's transaction commits, so a
/// broadcast never announces state that later rolled back. The API registers the SignalR-backed
/// implementation (one group per draft id); the default is a no-op so the Application layer, the in-memory
/// foundation, and every non-realtime test run unchanged without a hub.
/// </summary>
public interface IDraftNotifier
{
    Task PublishAsync(DraftUpdateNotification notification, CancellationToken cancellationToken);
}

/// <summary>The no-op default; replaced by the SignalR notifier in the API host.</summary>
public sealed class NullDraftNotifier : IDraftNotifier
{
    public Task PublishAsync(DraftUpdateNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
