using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

// The pre-draft five-star club + protected-player round (PR-14, PRD §9.5, DRAFT_RULES §1–5). Each command
// follows the PR-12/PR-13 slice shape: it carries the last-seen ExpectedVersion, runs read → validate →
// mutate → append through one ITransactionRunner scope, and returns the enriched snapshot. The round runs in
// straight spinner order; club uniqueness, footballer eligibility (75+, from the chosen club), and global
// availability are enforced here server-side and, transactionally, by the unique indexes.

/// <summary>Opens the club round (host-only, SpinnerRanking → ClubSelection) once the spinner order is committed.</summary>
public sealed record OpenClubSelectionCommand(Guid DraftId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

/// <summary>
/// Records the active team's combined five-star club + protected-player choice (either teammate may submit in
/// 2v2; the first valid server-accepted submission wins). Straight spinner order, ClubSelection state only.
/// </summary>
public sealed record SelectClubAndProtectCommand(
    Guid DraftId, Guid ClubId, int FootballerId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

/// <summary>Opens the position draft (host-only, ClubSelection → PositionDraft) once every team has locked its club + held player.</summary>
public sealed record OpenPositionDraftCommand(Guid DraftId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

public sealed class OpenClubSelectionCommandValidator : AbstractValidator<OpenClubSelectionCommand>
{
    public OpenClubSelectionCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class SelectClubAndProtectCommandValidator : AbstractValidator<SelectClubAndProtectCommand>
{
    public SelectClubAndProtectCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ClubId).NotEmpty();
        RuleFor(command => command.FootballerId).GreaterThan(0);
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class OpenPositionDraftCommandValidator : AbstractValidator<OpenPositionDraftCommand>
{
    public OpenPositionDraftCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class OpenClubSelectionCommandHandler(
    IDraftStore drafts, IIdentityService identity, IDraftCatalog catalog, ITransactionRunner transaction)
    : IRequestHandler<OpenClubSelectionCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(OpenClubSelectionCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);

            if (draft.Status != DraftStatus.SpinnerRanking)
            {
                throw FormationGuards.Validation("status", $"Club selection opens from spinner ranking (the draft is {draft.Status}).");
            }
            if (draft.Teams.Count == 0 || draft.Teams.Any(team => team.SpinnerRank is null))
            {
                throw FormationGuards.Validation("spinner", "Commit the spinner order before opening club selection.");
            }

            draft.OpenClubSelection(request.ActorUserId);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken, catalog);
    }
}

public sealed class SelectClubAndProtectCommandHandler(
    IDraftStore drafts, IIdentityService identity, IDraftCatalog catalog, ITransactionRunner transaction)
    : IRequestHandler<SelectClubAndProtectCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(SelectClubAndProtectCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            if (draft.Status != DraftStatus.ClubSelection)
            {
                throw FormationGuards.Validation("status", $"A club can only be chosen during club selection (the draft is {draft.Status}).");
            }

            // Straight spinner order: the active team is the lowest committed rank without a club yet.
            var activeTeam = DraftTurn.ActiveClubTeam(draft)
                ?? throw FormationGuards.Validation("turn", "Every team has already chosen a club.");
            var (actorParticipant, actorTeam) = DraftActor.Resolve(draft, request.ActorUserId);
            DraftActor.EnsureMayPickFor(activeTeam, actorTeam, request.ActorIsAdmin);

            // Club must be an eligible five-star club in the pinned dataset and not already taken in this lobby.
            var club = await catalog.FindFiveStarClubAsync(draft.PinnedDatasetVersionId, request.ClubId, ct)
                ?? throw FormationGuards.Validation("club", "That club is not an eligible five-star club.");
            if (draft.Teams.Any(team => team.SelectedClubId == request.ClubId))
            {
                throw FormationGuards.Validation("club", "That club has already been chosen by another team.");
            }

            // Protected footballer must be eligible (75+, Kick Off), from the chosen club, and still available.
            var footballer = await catalog.FindFootballerAsync(draft.PinnedDatasetVersionId, request.FootballerId, ct)
                ?? throw FormationGuards.Validation("footballer", "That footballer is not eligible (75+ men's base/Kick Off).");
            if (footballer.ClubId != request.ClubId)
            {
                throw FormationGuards.Validation("footballer", $"The protected player must play for {club.Name}.");
            }
            if (draft.Picks.Any(pick => pick.FootballerId == request.FootballerId))
            {
                throw FormationGuards.Validation("footballer", "That footballer has already been taken.");
            }

            draft.SelectClubAndProtect(
                activeTeam.Id,
                request.ClubId,
                new DraftPickFootballer(footballer.Id, footballer.Name, footballer.Overall, footballer.Positions.FirstOrDefault()),
                actorParticipant?.Id,
                request.ActorUserId);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken, catalog);
    }
}

public sealed class OpenPositionDraftCommandHandler(
    IDraftStore drafts, IIdentityService identity, IDraftCatalog catalog, ITransactionRunner transaction,
    TimeProvider clock)
    : IRequestHandler<OpenPositionDraftCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(OpenPositionDraftCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);

            if (draft.Status != DraftStatus.ClubSelection)
            {
                throw FormationGuards.Validation("status", $"The position draft opens from club selection (the draft is {draft.Status}).");
            }

            // Cannot begin until EVERY team has locked its club and its protected/held player (DRAFT_RULES §5).
            var everyoneReady = draft.Teams.Count > 0 && draft.Teams.All(team =>
                team.SelectedClubId is not null
                && draft.Picks.Any(pick => pick.DraftTeamId == team.Id && pick.SlotOrder == Draft.HeldSlotOrder));
            if (!everyoneReady)
            {
                throw FormationGuards.Validation("clubs", "Every team must choose a club and protect a player first.");
            }

            draft.OpenPositionDraft(request.ActorUserId);
            // The first position turn's 120s clock starts the moment the round opens (PR-16, PRD §6.4).
            draft.StartTurnClock(clock.GetUtcNow());
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken, catalog, clock);
    }
}

/// <summary>
/// Shared pick authority for the club and position rounds (DRAFT_RULES decision 6): a pick may be submitted by
/// any teammate of the active team (2v2 first-valid-wins), or by an admin. Resolves the actor's team from
/// their participant membership so the same rule serves both rounds.
/// </summary>
internal static class DraftActor
{
    public static (DraftParticipant? Participant, DraftTeam? Team) Resolve(Draft draft, Guid actorUserId)
    {
        var participant = draft.Participants.FirstOrDefault(candidate => candidate.UserId == actorUserId);
        if (participant is null)
        {
            return (null, null);
        }

        var team = draft.Teams.FirstOrDefault(candidate => candidate.Members.Any(member => member.ParticipantId == participant.Id));
        return (participant, team);
    }

    public static void EnsureMayPickFor(DraftTeam activeTeam, DraftTeam? actorTeam, bool actorIsAdmin)
    {
        if (actorIsAdmin)
        {
            return;
        }
        if (actorTeam is null)
        {
            throw new ForbiddenAppException("You are not on a team in this draft.");
        }
        if (actorTeam.Id != activeTeam.Id)
        {
            throw FormationGuards.Validation("turn", "It is not your team's turn.");
        }
    }
}
