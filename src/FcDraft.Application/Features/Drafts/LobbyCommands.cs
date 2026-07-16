using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

// The lobby lifecycle commands (PR-11). Each follows the PR-10 concurrency pattern: it carries the
// last-seen ExpectedVersion, runs read → validate → mutate → append in one ITransactionRunner scope, and
// appends exactly one DraftEvent so the lobby's attendance is fully auditable. Host-only actions check the
// caller is the host or an admin; capacity rules (PRD §6.2) are enforced here, server-side.

/// <summary>Invites an active user into an open lobby (host-only). Rejects deactivated users and over-capacity.</summary>
public sealed record InviteParticipantCommand(
    Guid DraftId, Guid InviteUserId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

/// <summary>Marks the calling participant present in the lobby (self-service presence). Idempotent if already joined.</summary>
public sealed record JoinDraftCommand(Guid DraftId, int ExpectedVersion, Guid ActorUserId) : IRequest<DraftDetail>;

/// <summary>Removes a participant from the lobby before start (host-only). The host cannot remove themselves.</summary>
public sealed record RemoveParticipantCommand(
    Guid DraftId, Guid ParticipantUserId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

/// <summary>Locks a confirmed lobby into team formation (host-only), enforcing the full capacity rules.</summary>
public sealed record LockLobbyCommand(Guid DraftId, int ExpectedVersion, Guid ActorUserId, bool ActorIsAdmin = false)
    : IRequest<DraftDetail>;

public sealed class InviteParticipantCommandValidator : AbstractValidator<InviteParticipantCommand>
{
    public InviteParticipantCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.InviteUserId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class JoinDraftCommandValidator : AbstractValidator<JoinDraftCommand>
{
    public JoinDraftCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class RemoveParticipantCommandValidator : AbstractValidator<RemoveParticipantCommand>
{
    public RemoveParticipantCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ParticipantUserId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class LockLobbyCommandValidator : AbstractValidator<LockLobbyCommand>
{
    public LockLobbyCommandValidator()
    {
        RuleFor(command => command.DraftId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
    }
}

public sealed class InviteParticipantCommandHandler(
    IDraftStore drafts, IIdentityService identity, ITransactionRunner transaction, DraftParticipantNotifier lifecycle)
    : IRequestHandler<InviteParticipantCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(InviteParticipantCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            LobbyGuards.EnsureOpenLobby(draft);

            if (draft.Participants.Any(participant => participant.UserId == request.InviteUserId))
            {
                throw LobbyGuards.Validation("invite", "That user is already in this lobby.");
            }

            if (!LobbyCapacity.CanAdmitAnother(draft.Format, draft.Participants.Count))
            {
                var (_, max) = LobbyCapacity.Bounds(draft.Format);
                throw LobbyGuards.Validation("invite", $"This lobby is full (maximum {max} participants).");
            }

            var invitee = await identity.FindByIdAsync(request.InviteUserId, ct)
                ?? throw LobbyGuards.Validation("invite", "That account no longer exists.");
            if (invitee.Status != AccountStatus.Active)
            {
                throw LobbyGuards.Validation("invite", $"{invitee.DisplayName} is deactivated and cannot be invited.");
            }

            draft.InviteParticipant(invitee.Id, request.ActorUserId);
            // Same transaction (PR-20): the invite notification + outbox email commit with the mutation.
            await lifecycle.NotifyInvitedAsync(draft, invitee, ct);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }
}

public sealed class JoinDraftCommandHandler(
    IDraftStore drafts, IIdentityService identity, ITransactionRunner transaction)
    : IRequestHandler<JoinDraftCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(JoinDraftCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            LobbyGuards.EnsureOpenLobby(draft);

            var participant = draft.Participants.FirstOrDefault(candidate => candidate.UserId == request.ActorUserId)
                ?? throw new ForbiddenAppException("You have not been invited to this lobby.");

            var account = await identity.FindByIdAsync(request.ActorUserId, ct);
            if (account is null || account.Status != AccountStatus.Active)
            {
                throw new ForbiddenAppException("A deactivated account cannot join a lobby.");
            }

            // Presence is idempotent: a second join (double-tap, reconnect) is accepted as a no-op so it
            // does not raise a spurious conflict or append a duplicate event.
            if (participant.Status != DraftParticipantStatus.Joined)
            {
                draft.MarkParticipantJoined(request.ActorUserId);
                await drafts.SaveChangesAsync(ct);
            }

            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }
}

public sealed class RemoveParticipantCommandHandler(
    IDraftStore drafts, IIdentityService identity, ITransactionRunner transaction)
    : IRequestHandler<RemoveParticipantCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(RemoveParticipantCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            LobbyGuards.EnsureOpenLobby(draft);

            var participant = draft.Participants.FirstOrDefault(candidate => candidate.UserId == request.ParticipantUserId)
                ?? throw new KeyNotFoundException("That user is not a participant of this lobby.");
            if (participant.IsHost)
            {
                throw LobbyGuards.Validation("participant", "The host cannot be removed from their own lobby.");
            }

            draft.RemoveParticipant(request.ParticipantUserId, request.ActorUserId);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }
}

public sealed class LockLobbyCommandHandler(
    IDraftStore drafts, IIdentityService identity, ITransactionRunner transaction)
    : IRequestHandler<LockLobbyCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(LockLobbyCommand request, CancellationToken cancellationToken)
    {
        var draft = await transaction.ExecuteAsync(async ct =>
        {
            var draft = await drafts.FindAsync(request.DraftId, ct)
                ?? throw new KeyNotFoundException("Draft not found.");

            DraftGuards.EnsureActorMayControl(draft, request.ActorUserId, request.ActorIsAdmin);
            DraftGuards.EnsureExpectedVersion(draft, request.ExpectedVersion);
            LobbyGuards.EnsureOpenLobby(draft);

            if (!LobbyCapacity.IsLockable(draft.Format, draft.Participants.Count))
            {
                var (min, max) = LobbyCapacity.Bounds(draft.Format);
                var even = LobbyCapacity.RequiresEven(draft.Format) ? ", an even number of" : "";
                throw LobbyGuards.Validation(
                    "capacity",
                    $"A {DraftFormats.ToWire(draft.Format)} lobby needs between {min} and {max}{even} participants before it can start.");
            }

            draft.LockLobby(request.ActorUserId);
            await drafts.SaveChangesAsync(ct);
            return draft;
        }, cancellationToken);

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }
}

/// <summary>Shared lobby-state guards for the lifecycle handlers.</summary>
internal static class LobbyGuards
{
    public static void EnsureOpenLobby(Draft draft)
    {
        if (draft.Status != DraftStatus.Lobby)
        {
            throw Validation("status", $"This lobby is no longer open for changes (it is {draft.Status}).");
        }
    }

    public static ValidationAppException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
