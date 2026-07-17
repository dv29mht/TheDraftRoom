using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// Submits one position pick (PR-15, PRD §9.6, DRAFT_RULES §2/§5). Host-agnostic: any teammate of the active
/// team (2v2 first-valid-wins) or an admin may submit. The active (team, slot) is derived from committed
/// spinner ranks by <b>snake</b> order (<see cref="DraftTurnOrder"/>) over the frozen roster snapshot; the pick
/// must match the slot's position (a flexible bench slot accepts any), be 75+ and in the pinned dataset, and
/// still be available. Turn/version/eligibility/uniqueness are enforced here and, transactionally, by the
/// unique indexes so a duplicate/stale/out-of-turn race loses. Filling the final slot completes the draft.
/// The 120s clock (PR-16) is enforced first: an expired turn auto-picks before this submission is judged, so
/// a too-late pick surfaces as the stale-version conflict it is.
/// </summary>
public sealed record SubmitPickCommand(
    Guid DraftId, int FootballerId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

public sealed class SubmitPickCommandValidator : AbstractValidator<SubmitPickCommand>
{
    public SubmitPickCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.FootballerId).GreaterThan(0);
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class SubmitPickCommandHandler(
    IDraftStore drafts, IIdentityService identity, IDraftCatalog catalog, ITransactionRunner transaction,
    DraftExpiryService expiry, TimeProvider clock, DraftParticipantNotifier lifecycle,
    IProductAnalytics? analytics = null)
    : IRequestHandler<SubmitPickCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(SubmitPickCommand request, CancellationToken cancellationToken)
    {
        // Lazy expiry enforcement (PR-16): an overdue turn auto-picks in its own committed transaction
        // first, so this submission then fails the version check — the timer won, not the teammate.
        await expiry.CatchUpAsync(request.DraftId, cancellationToken);

        // §15 samples captured inside the transaction, recorded only after it commits.
        var samples = default(PickAnalyticsSamples);

        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            if (draft.Status != DraftStatus.PositionDraft)
            {
                throw FormationGuards.Validation("status", $"Picks are only accepted during the position draft (the draft is {draft.Status}).");
            }

            // Snake turn order: the active (team, slot) is a pure function of committed ranks + picks made.
            var active = DraftTurn.ActivePosition(draft)
                ?? throw FormationGuards.Validation("turn", "There is no open slot to pick for.");
            var (activeTeam, slot) = active;
            var (actorParticipant, actorTeam) = DraftActor.Resolve(draft, request.ActorUserId);
            DraftActor.EnsureMayPickFor(activeTeam, actorTeam, request.ActorIsAdmin);

            // Eligible in the pinned dataset (75+, Kick Off); the shared engine checks slot fit and
            // availability, records the pick, and advances the turn clock (or completes the draft).
            var footballer = await catalog.FindFootballerAsync(draft.PinnedDatasetVersionId, request.FootballerId, ct)
                ?? throw FormationGuards.Validation("footballer", "That footballer is not eligible (75+ men's base/Kick Off).");

            var now = clock.GetUtcNow();
            samples = PickAnalyticsSamples.Before(draft, acceptedAt: now);

            PickEngine.Accept(
                draft, activeTeam, slot, footballer,
                actorParticipant?.Id, request.ActorUserId, isAutoPick: false, nextTurnAnchor: now);

            // The final pick completed the draft: every participant's result notification + outbox email
            // commit with it (PR-20).
            if (draft.Status == DraftStatus.Completed)
            {
                await lifecycle.NotifyCompletedAsync(draft, ct);
            }

            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        samples.Record(analytics ?? NullProductAnalytics.Instance, auto: false, completed: draft.Status == DraftStatus.Completed);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken, catalog, clock);
    }
}

/// <summary>
/// The §15 facts about one accepted position pick, captured against the pre-accept draft state so
/// they can be recorded AFTER the transaction commits (a rolled-back pick records nothing).
/// </summary>
internal readonly record struct PickAnalyticsSamples(
    string Format, bool IsFirstPositionPick, double SecondsFromCreation, double? TurnSeconds)
{
    public static PickAnalyticsSamples Before(Draft draft, DateTimeOffset acceptedAt) => new(
        DraftFormats.ToWire(draft.Format),
        !draft.Picks.Any(pick => pick.SlotOrder > Draft.HeldSlotOrder),
        Math.Max(0, (acceptedAt - draft.CreatedAt).TotalSeconds),
        draft.TurnStartedAt is { } anchor ? Math.Max(0, (acceptedAt - anchor).TotalSeconds) : null);

    public void Record(IProductAnalytics analytics, bool auto, bool completed)
    {
        if (Format is null)
        {
            return; // default(...) — the transaction rejected the pick before the capture point.
        }

        analytics.PickAccepted(Format, auto, TurnSeconds);
        if (IsFirstPositionPick)
        {
            analytics.FirstPick(Format, SecondsFromCreation);
        }

        if (completed)
        {
            analytics.DraftEnded(Format, "completed");
        }
    }
}
