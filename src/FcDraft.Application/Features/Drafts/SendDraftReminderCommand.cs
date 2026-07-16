using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// The host-initiated draft reminder (PR-20, §9.8 — the recorded MVP trigger: the HOST decides when a
/// nudge is worth sending; no scheduler exists yet). Notifies every other participant in-app and, for
/// those who have not opted out of optional emails (§9.9), by email through the outbox. It mutates no
/// draft state, so it carries no ExpectedVersion and appends no draft event — the notification rows are
/// the record. Returns how many participants were reminded.
/// </summary>
public sealed record SendDraftReminderCommand(Guid DraftId, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<int>;

public sealed class SendDraftReminderCommandValidator : AbstractValidator<SendDraftReminderCommand>
{
    public SendDraftReminderCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class SendDraftReminderCommandHandler(
    IDraftStore drafts, ITransactionRunner transaction, DraftParticipantNotifier lifecycle)
    : IRequestHandler<SendDraftReminderCommand, int>
{
    public Task<int> Handle(SendDraftReminderCommand request, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            if (draft.Status is not (DraftStatus.Lobby or DraftStatus.TeamFormation or DraftStatus.ReadyCheck))
            {
                throw LobbyGuards.Validation(
                    "status", $"Reminders are for drafts that have not started (this draft is {draft.Status}).");
            }

            return await lifecycle.NotifyReminderAsync(draft, request.ActorUserId, ct);
        }, cancellationToken);
}
