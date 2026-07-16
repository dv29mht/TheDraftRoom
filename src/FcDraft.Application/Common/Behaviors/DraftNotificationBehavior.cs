using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using MediatR;

namespace FcDraft.Application.Common.Behaviors;

/// <summary>
/// Publishes one live update per accepted draft mutation (PR-17, PRD §17.7). Runs after the handler
/// returns — every draft command commits its transaction inside the handler, so a broadcast can never
/// announce state that rolled back — and only for requests named <c>*Command</c> (queries never publish).
/// A command returning the full <see cref="DraftDetail"/> broadcasts the authoritative snapshot; one
/// returning only a <see cref="DraftSummary"/> broadcasts the version-stamped envelope and clients refetch.
/// Multi-event actions (club+protect, spinner commit) bump the version once and broadcast once, stamped
/// with the latest event. The timer's own auto-picks are published by <see cref="DraftExpiryService"/>.
/// </summary>
public sealed class DraftNotificationBehavior<TRequest, TResponse>(IDraftNotifier notifier)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        if (typeof(TRequest).Name.EndsWith("Command", StringComparison.Ordinal))
        {
            var notification = response switch
            {
                DraftDetail detail => new DraftUpdateNotification(
                    detail.Summary.Id,
                    detail.Summary.Version,
                    detail.Events.Count > 0 ? detail.Events[^1].Type : "DraftUpdated",
                    detail),
                DraftSummary summary => new DraftUpdateNotification(summary.Id, summary.Version, "DraftUpdated", null),
                _ => null,
            };

            if (notification is not null)
            {
                await notifier.PublishAsync(notification, cancellationToken);
            }
        }

        return response;
    }
}
