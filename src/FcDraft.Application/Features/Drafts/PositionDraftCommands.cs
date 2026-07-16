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
/// (No 120s timer or auto-pick — that is PR-16.)
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
    IDraftStore drafts, IIdentityService identity, IDraftCatalog catalog, ITransactionRunner transaction)
    : IRequestHandler<SubmitPickCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(SubmitPickCommand request, CancellationToken cancellationToken)
    {
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

            // Eligible in the pinned dataset (75+, Kick Off), fills the slot's position, and still available.
            var footballer = await catalog.FindFootballerAsync(draft.PinnedDatasetVersionId, request.FootballerId, ct)
                ?? throw FormationGuards.Validation("footballer", "That footballer is not eligible (75+ men's base/Kick Off).");

            var acceptsAny = slot.SlotType == RosterSlotType.FlexBench || slot.Position is null;
            if (!acceptsAny && !footballer.Positions.Any(position => string.Equals(position, slot.Position, StringComparison.OrdinalIgnoreCase)))
            {
                throw FormationGuards.Validation("footballer", $"{footballer.Name} cannot play {slot.Position}.");
            }
            if (draft.Picks.Any(pick => pick.FootballerId == request.FootballerId))
            {
                throw FormationGuards.Validation("footballer", "That footballer has already been taken.");
            }

            draft.AcceptPick(
                activeTeam.Id,
                slot.Order,
                new DraftPickFootballer(footballer.Id, footballer.Name, footballer.Overall, footballer.Positions.FirstOrDefault()),
                actorParticipant?.Id,
                request.ActorUserId);

            // The final slot fills the last squad — complete the draft in the same transaction.
            if (DraftTurn.ActivePosition(draft) is null)
            {
                draft.CompleteDraft(request.ActorUserId);
            }

            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken, catalog);
    }
}
