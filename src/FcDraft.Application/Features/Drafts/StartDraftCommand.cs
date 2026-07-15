using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// Starts a draft: the <see cref="DraftStatus.ReadyCheck"/> → <see cref="DraftStatus.SpinnerRanking"/>
/// transition where configuration is frozen (PRD §9.4). Snapshots the draft's own bound roster template's
/// ordered slots and pick timer (the template the host chose at creation, PR-11) and pins the active
/// dataset version, so later template/dataset edits cannot mutate this in-progress draft, then records
/// <see cref="DraftEventType.DraftStarted"/>. Presence/team/readiness gating is layered on by PR-12–PR-13.
/// </summary>
public sealed record StartDraftCommand(Guid DraftId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftSummary>;

public sealed class StartDraftCommandValidator : AbstractValidator<StartDraftCommand>
{
    public StartDraftCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class StartDraftCommandHandler(
    IDraftStore drafts,
    IRosterTemplateService templates,
    IDatasetAdminService datasets,
    ITransactionRunner transaction)
    : IRequestHandler<StartDraftCommand, DraftSummary>
{
    public async Task<DraftSummary> Handle(StartDraftCommand request, CancellationToken cancellationToken)
    {
        var datasetVersionId = await ResolveActiveDatasetVersionAsync(cancellationToken);

        return await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);

            // Freeze the template the host bound at creation — not whatever is active now — so a template
            // activation between creation and start cannot swap this draft's roster out from under it.
            var template = await templates.GetAsync(draft.RosterTemplateId, ct)
                ?? throw new ValidationAppException(new Dictionary<string, string[]>
                {
                    ["rosterTemplate"] = ["This draft's roster template no longer exists; it cannot start."],
                });

            if (!DraftStateMachine.IsAllowed(draft.Status, DraftStatus.SpinnerRanking))
            {
                throw new ValidationAppException(new Dictionary<string, string[]>
                {
                    ["status"] = [$"A draft can only start from the ready check (it is currently {draft.Status})."],
                });
            }

            var slots = template.Slots.Select(slot => new DraftRosterSlot
            {
                DraftId = draft.Id,
                Order = slot.Order,
                SlotType = Enum.Parse<RosterSlotType>(slot.SlotType, ignoreCase: true),
                Position = slot.Position,
                Label = slot.Label,
            });
            draft.SnapshotConfiguration(slots, template.Summary.PickTimerSeconds, datasetVersionId);
            draft.Transition(DraftStatus.SpinnerRanking, DraftEventType.DraftStarted, request.ActorUserId);

            await drafts.SaveChangesAsync(ct);
            return DraftMapper.ToSummary(draft);
        }, cancellationToken);
    }

    // Pin the version currently marked Active (the in-memory foundation reports the bundled snapshot as
    // active; a database configuration returns the activated version). Null when no version exists yet.
    private async Task<Guid?> ResolveActiveDatasetVersionAsync(CancellationToken cancellationToken)
    {
        var versions = await datasets.ListVersionsAsync(cancellationToken);
        var active = versions.FirstOrDefault(version =>
            string.Equals(version.Status, "Active", StringComparison.OrdinalIgnoreCase));
        return active?.Id;
    }
}
