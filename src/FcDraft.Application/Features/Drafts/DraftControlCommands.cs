using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

// Host controls and admin recovery (PR-16, PRD §9.6–§9.7, §9.10). Each command follows the slice shape:
// last-seen ExpectedVersion, read → validate → mutate → append in one ITransactionRunner scope, one event
// per accepted change, and the enriched snapshot returned. Pause/resume/cancel are host-or-admin
// (DraftGuards.EnsureActorMayControl); recovery is admin-only and always compensating — the original
// history is never edited or deleted.

/// <summary>Pauses a live draft (ClubSelection/PositionDraft → Paused) with a required reason; the pick clock freezes.</summary>
public sealed record PauseDraftCommand(
    Guid DraftId, string Reason, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

/// <summary>Resumes a paused draft back to the state it paused from; paused time never elapses from the turn clock.</summary>
public sealed record ResumeDraftCommand(
    Guid DraftId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false, string? Reason = null)
    : IRequest<DraftDetail>;

/// <summary>Cancels a draft (→ Cancelled) with a required reason. History is preserved — cancellation only appends.</summary>
public sealed record CancelDraftCommand(
    Guid DraftId, string Reason, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

/// <summary>
/// Applies an audited, admin-only recovery action (PRD §9.7/§9.10): appends a compensating
/// <c>AdminRecoveryApplied</c> event with a required reason, optionally restoring the active turn's clock
/// to a full timer (<see cref="RestartTurnClock"/>) so a stuck draft can continue fairly.
/// </summary>
public sealed record ApplyAdminRecoveryCommand(
    Guid DraftId, string Reason, bool RestartTurnClock, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

public sealed class PauseDraftCommandValidator : AbstractValidator<PauseDraftCommand>
{
    public PauseDraftCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(512);
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class ResumeDraftCommandValidator : AbstractValidator<ResumeDraftCommand>
{
    public ResumeDraftCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.Reason).MaximumLength(512);
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class CancelDraftCommandValidator : AbstractValidator<CancelDraftCommand>
{
    public CancelDraftCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(512);
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class ApplyAdminRecoveryCommandValidator : AbstractValidator<ApplyAdminRecoveryCommand>
{
    public ApplyAdminRecoveryCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(512);
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class PauseDraftCommandHandler(
    IDraftStore drafts, IIdentityService identity, IDraftCatalog catalog, ITransactionRunner transaction,
    DraftExpiryService expiry, TimeProvider clock)
    : IRequestHandler<PauseDraftCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(PauseDraftCommand request, CancellationToken cancellationToken)
    {
        // An already-expired turn auto-picks first (it expired before the pause); the pause then carries a
        // stale version and surfaces as a 409 the client resolves by refreshing — the timer, not the pause,
        // won that race.
        await expiry.CatchUpAsync(request.DraftId, cancellationToken);

        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            if (!DraftStateMachine.IsAllowed(draft.Status, DraftStatus.Paused))
            {
                throw FormationGuards.Validation("status", $"A draft can only pause from a live round (the draft is {draft.Status}).");
            }

            draft.PauseDraft(request.ActorUserId, request.Reason.Trim(), clock.GetUtcNow());
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken, catalog, clock);
    }
}

public sealed class ResumeDraftCommandHandler(
    IDraftStore drafts, IIdentityService identity, IDraftCatalog catalog, ITransactionRunner transaction,
    TimeProvider clock)
    : IRequestHandler<ResumeDraftCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(ResumeDraftCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            if (draft.Status != DraftStatus.Paused)
            {
                throw FormationGuards.Validation("status", $"Only a paused draft can resume (the draft is {draft.Status}).");
            }

            // Resume returns to the state the draft paused from — recorded by the latest DraftPaused event.
            var lastPause = draft.Events
                .Where(evt => evt.Type == DraftEventType.DraftPaused)
                .OrderBy(evt => evt.Sequence)
                .LastOrDefault();
            var target = lastPause?.FromStatus
                ?? throw FormationGuards.Validation("status", "This draft has no recorded pause to resume from.");
            if (!DraftStateMachine.IsAllowed(DraftStatus.Paused, target))
            {
                throw FormationGuards.Validation("status", $"This draft cannot resume to {target}.");
            }

            draft.ResumeDraft(target, request.ActorUserId, request.Reason?.Trim(), clock.GetUtcNow());
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken, catalog, clock);
    }
}

public sealed class CancelDraftCommandHandler(
    IDraftStore drafts, IIdentityService identity, IDraftCatalog catalog, ITransactionRunner transaction,
    TimeProvider clock, DraftParticipantNotifier lifecycle)
    : IRequestHandler<CancelDraftCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(CancelDraftCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            if (!DraftStateMachine.IsAllowed(draft.Status, DraftStatus.Cancelled))
            {
                throw FormationGuards.Validation("status", $"A {draft.Status} draft cannot be cancelled.");
            }

            draft.CancelDraft(request.ActorUserId, request.Reason.Trim());
            // Same transaction (PR-20): every participant's cancellation notice + outbox email commit
            // with the cancellation itself.
            await lifecycle.NotifyCancelledAsync(draft, request.Reason.Trim(), ct);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken, catalog, clock);
    }
}

public sealed class ApplyAdminRecoveryCommandHandler(
    IDraftStore drafts, IIdentityService identity, IDraftCatalog catalog, ITransactionRunner transaction,
    TimeProvider clock)
    : IRequestHandler<ApplyAdminRecoveryCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(ApplyAdminRecoveryCommand request, CancellationToken cancellationToken)
    {
        // §9.7: recovery is separately permissioned — admin only, never the host.
        if (!request.ActorIsAdmin)
        {
            throw new ForbiddenAppException("Only an administrator can apply draft recovery.");
        }

        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            draft.ApplyAdminRecovery(
                request.ActorUserId,
                request.Reason.Trim(),
                request.RestartTurnClock ? clock.GetUtcNow() : null);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken, catalog, clock);
    }
}
