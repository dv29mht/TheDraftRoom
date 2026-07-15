using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// The general-purpose lifecycle transition primitive (PR-10). Moves a draft to <paramref name="TargetStatus"/>
/// if §10.1 allows it, recording <paramref name="EventType"/> as the accepted event. The whole read → validate →
/// mutate → save runs in one transaction so a rejected move leaves no partial write. Semantic, per-stage
/// endpoints (invite, form teams, pause, cancel …) are layered on by PR-11+; starting a draft uses
/// <see cref="StartDraftCommand"/> so configuration is snapshotted, so this command rejects a move to
/// <see cref="DraftStatus.SpinnerRanking"/>.
/// </summary>
public sealed record TransitionDraftCommand(
    Guid DraftId,
    string TargetStatus,
    string EventType,
    int ExpectedVersion,
    Guid ActorUserId,
    bool ActorIsAdmin = false,
    string? Reason = null,
    string? Payload = null) : IRequest<DraftSummary>;

public sealed class TransitionDraftCommandValidator : AbstractValidator<TransitionDraftCommand>
{
    public TransitionDraftCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.TargetStatus)
            .Must(status => Enum.TryParse<DraftStatus>(status, ignoreCase: true, out _))
            .WithMessage("Unknown target status.");
        RuleFor(command => command.EventType)
            .Must(type => Enum.TryParse<DraftEventType>(type, ignoreCase: true, out _))
            .WithMessage("Unknown draft event type.");
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class TransitionDraftCommandHandler(IDraftStore drafts, ITransactionRunner transaction)
    : IRequestHandler<TransitionDraftCommand, DraftSummary>
{
    public async Task<DraftSummary> Handle(TransitionDraftCommand request, CancellationToken cancellationToken)
    {
        var target = Enum.Parse<DraftStatus>(request.TargetStatus, ignoreCase: true);
        var eventType = Enum.Parse<DraftEventType>(request.EventType, ignoreCase: true);

        if (target == DraftStatus.SpinnerRanking)
        {
            throw new ValidationAppException(new Dictionary<string, string[]>
            {
                ["targetStatus"] = ["Starting a draft (moving to SpinnerRanking) uses the start-draft command so configuration is snapshotted."],
            });
        }

        return await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);

            if (!DraftStateMachine.IsAllowed(draft.Status, target))
            {
                throw new ValidationAppException(new Dictionary<string, string[]>
                {
                    ["status"] = [$"A draft cannot move from {draft.Status} to {target}."],
                });
            }

            draft.Transition(target, eventType, request.ActorUserId, request.Reason?.Trim(), request.Payload);
            await drafts.SaveChangesAsync(ct);
            return DraftMapper.ToSummary(draft);
        }, cancellationToken);
    }
}
