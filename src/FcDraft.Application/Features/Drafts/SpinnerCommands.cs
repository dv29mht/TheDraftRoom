using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// Commits the server-authoritative spinner order (PR-13, PRD §9.5): host-only, in the
/// <see cref="DraftStatus.SpinnerRanking"/> state, it shuffles the formed teams through the injected
/// <see cref="IShuffler"/> and assigns each a unique <see cref="DraftTeam.SpinnerRank"/>, appending
/// <see cref="DraftEventType.SpinnerOrderCommitted"/> then <see cref="DraftEventType.SpinnerOrderRevealed"/>.
/// Idempotent: once an order is committed a retry returns it unchanged and never reshuffles. The animated
/// wheel merely reveals this result, so it can never influence it. (The SpinnerRanking → ClubSelection
/// transition is PR-14.)
/// </summary>
public sealed record CommitSpinnerCommand(Guid DraftId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

public sealed class CommitSpinnerCommandValidator : AbstractValidator<CommitSpinnerCommand>
{
    public CommitSpinnerCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class CommitSpinnerCommandHandler(
    IDraftStore drafts, IIdentityService identity, IShuffler shuffler, ITransactionRunner transaction)
    : IRequestHandler<CommitSpinnerCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(CommitSpinnerCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);

            if (draft.Status != DraftStatus.SpinnerRanking)
            {
                throw FormationGuards.Validation("status", $"The spinner can only run during spinner ranking (the draft is {draft.Status}).");
            }

            // Idempotent: a committed order is never reshuffled. A retry (at the current version) is a no-op
            // that returns the same ranks; the version token turns a stale retry into a 409 before this.
            if (draft.Teams.All(team => team.SpinnerRank is not null) && draft.Teams.Count > 0)
            {
                return draft;
            }
            if (draft.Teams.Count == 0)
            {
                throw FormationGuards.Validation("teams", "There are no teams to rank.");
            }

            var order = draft.Teams.Select(team => team.Id).ToList();
            shuffler.Shuffle(order);
            draft.CommitSpinnerOrder(order, request.ActorUserId);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }
}
