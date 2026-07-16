using System.Collections.Concurrent;
using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// Applies overdue timer expiries (PR-16, PRD §6.4). The service runs on a single-instance host that can
/// scale to zero, so no in-process countdown can be the authority: expiry is evaluated LAZILY — the
/// board/get/pick/pause paths call <see cref="CatchUpAsync"/> before acting — plus opportunistically by the
/// hosted sweep while the instance is warm. Each expired, unpaused position turn auto-picks the
/// deterministic §6.4 best (via <see cref="PickEngine"/>) with the next anchor at the expired deadline, so
/// however late the evaluation runs, every missed turn consumed exactly its allotted seconds and every
/// evaluator computes the same picks. Concurrent triggers are safe: the version token and the unique
/// (team, slot)/(draft, footballer) indexes let exactly ONE catch-up commit; the loser's conflict is
/// swallowed here — and because the auto-pick is a pure function of the same committed state, the loser had
/// computed the identical picks, so even its local (rolled-back) view matches what the winner committed.
/// </summary>
public sealed class DraftExpiryService(
    IDraftStore drafts,
    IDraftCatalog catalog,
    IIdentityService identity,
    ITransactionRunner transaction,
    IDraftNotifier notifier,
    TimeProvider clock,
    DraftParticipantNotifier lifecycle)
{
    // Serializes catch-ups per draft within this (single-instance) process, so the sweep and a read
    // arriving together do not race each other: the database constraints remain the cross-transaction
    // guarantee, this just makes the common collision quiet — and keeps the lock-free in-memory store
    // (which has no rollback) correct under the same collision.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    /// <summary>
    /// Applies every overdue auto-pick for the draft in one transaction (none is a cheap no-op), then
    /// publishes the authoritative snapshot. Never throws for a lost race — the caller re-reads and sees
    /// the winner's identical result.
    /// </summary>
    public async Task CatchUpAsync(Guid draftId, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(draftId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await CatchUpLockedAsync(draftId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task CatchUpLockedAsync(Guid draftId, CancellationToken cancellationToken)
    {
        Draft? updated;
        try
        {
            updated = await transaction.ExecuteAsync(async ct =>
            {
                var draft = await drafts.FindAsync(draftId, ct);
                if (draft is null)
                {
                    return null;
                }

                var applied = 0;
                var now = clock.GetUtcNow();
                while (draft.HasExpiredTurn(now))
                {
                    var expiredDeadline = draft.TurnDeadline!.Value;
                    var active = DraftTurn.ActivePosition(draft);
                    if (active is null)
                    {
                        break; // defensive: no open slot despite a live clock
                    }

                    var (team, slot) = active.Value;
                    var footballer = await PickEngine.ResolveAutoPickAsync(draft, slot, catalog, ct);
                    if (footballer is null)
                    {
                        break; // the pinned pool is exhausted for this slot — leave it to admin recovery
                    }

                    // System action: no participant, no actor. The next turn starts at the expired deadline
                    // (not "now"), so cascaded catch-ups after a cold start stay exact and deterministic.
                    PickEngine.Accept(draft, team, slot, footballer,
                        actorParticipantId: null, actorUserId: null, isAutoPick: true, nextTurnAnchor: expiredDeadline);
                    applied++;
                }

                if (applied == 0)
                {
                    return null;
                }

                // The catch-up's final auto-pick completed the draft: the result notifications + outbox
                // emails commit with it (PR-20) — the same guarantee a live pick gets.
                if (draft.Status == DraftStatus.Completed)
                {
                    await lifecycle.NotifyCompletedAsync(draft, ct);
                }

                await drafts.SaveChangesAsync(ct);
                return draft;
            }, cancellationToken);
        }
        catch (ConflictAppException)
        {
            // Another trigger (a concurrent read, pick, or the hosted sweep) committed this expiry first.
            // Exactly one pick per turn wins; nothing to do here.
            return;
        }

        if (updated is not null)
        {
            // After commit only: broadcast the authoritative state so connected clients see the auto-pick
            // (or the completion it caused) without polling.
            var detail = await LobbyProjection.ToDetailAsync(updated, identity, cancellationToken, catalog, clock);
            var lastEvent = detail.Events.Count > 0 ? detail.Events[^1].Type : nameof(DraftEventType.PickAutoSelected);
            await notifier.PublishAsync(
                new DraftUpdateNotification(detail.Summary.Id, detail.Summary.Version, lastEvent, detail),
                cancellationToken);
        }
    }
}
