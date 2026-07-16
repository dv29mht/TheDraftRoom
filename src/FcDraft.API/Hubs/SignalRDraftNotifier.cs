using FcDraft.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace FcDraft.API.Hubs;

/// <summary>
/// The SignalR-backed <see cref="IDraftNotifier"/> (PR-17): sends each committed draft update to the
/// draft's hub group. Delivery is best-effort by design — the transaction has already committed, REST
/// remains the authoritative snapshot, and clients reconcile on reconnect — so a transport failure is
/// logged and never fails the command that produced the update. Single-instance Cloud Run means the
/// in-process hub reaches every connection; no backplane is needed (or wanted) here.
/// </summary>
public sealed class SignalRDraftNotifier(IHubContext<DraftHub> hub, ILogger<SignalRDraftNotifier> logger)
    : IDraftNotifier
{
    /// <summary>The single client-facing message name; the payload is the version-stamped envelope.</summary>
    public const string DraftUpdatedMethod = "DraftUpdated";

    public async Task PublishAsync(DraftUpdateNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(DraftHub.GroupName(notification.DraftId))
                .SendAsync(DraftUpdatedMethod, notification, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to broadcast {EventType} v{Version} for draft {DraftId}; clients will reconcile via REST",
                notification.EventType, notification.Version, notification.DraftId);
        }
    }
}
