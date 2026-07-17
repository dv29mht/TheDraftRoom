using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// Creates a 1v1/2v2 tournament lobby (PRD §9.4). The creator becomes the host and is recorded as a joined
/// <see cref="DraftParticipant"/>, which opens the lobby (Draft → Lobby). The host binds a chosen roster
/// template (falling back to the active one) and may seed an initial invite list; each invitee is validated
/// (active account, within capacity) and recorded with a <see cref="DraftEventType.ParticipantInvited"/>
/// event. The whole creation runs in one transaction, so a rejected invite leaves no half-built lobby.
/// </summary>
public sealed record CreateDraftCommand(
    string Name,
    string Format,
    Guid HostUserId,
    Guid? RosterTemplateId = null,
    IReadOnlyList<Guid>? InviteUserIds = null) : IRequest<DraftDetail>;

public sealed class CreateDraftCommandValidator : AbstractValidator<CreateDraftCommand>
{
    public CreateDraftCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Format)
            .Must(format => DraftFormats.TryParse(format, out _))
            .WithMessage("Format must be '1v1' or '2v2'.");
        RuleFor(command => command.HostUserId).NotEmpty();
        RuleForEach(command => command.InviteUserIds).NotEmpty().When(command => command.InviteUserIds is not null);
        RuleFor(command => command.InviteUserIds)
            .Must(ids => ids!.Distinct().Count() == ids!.Count)
            .When(command => command.InviteUserIds is not null)
            .WithMessage("The invite list contains a duplicate user.");
        RuleFor(command => command)
            .Must(command => command.InviteUserIds is null || !command.InviteUserIds.Contains(command.HostUserId))
            .WithMessage("The host is added automatically and cannot also be invited.")
            .WithName(nameof(CreateDraftCommand.InviteUserIds));
    }
}

public sealed class CreateDraftCommandHandler(
    IDraftStore drafts,
    IRosterTemplateService templates,
    IIdentityService identity,
    ITransactionRunner transaction,
    DraftParticipantNotifier lifecycle,
    IProductAnalytics? analytics = null)
    : IRequestHandler<CreateDraftCommand, DraftDetail>
{
    public async Task<DraftDetail> Handle(CreateDraftCommand request, CancellationToken cancellationToken)
    {
        DraftFormats.TryParse(request.Format, out var format);

        // Bind the chosen template, or the active one when the host did not pick a specific template.
        var template = request.RosterTemplateId is { } chosenId
            ? await templates.GetAsync(chosenId, cancellationToken)
                ?? throw Validation("rosterTemplate", "The selected roster template no longer exists.")
            : await templates.GetActiveAsync(cancellationToken)
                ?? throw Validation("rosterTemplate", "No active roster template is configured; a draft cannot be created.");

        var host = await identity.FindByIdAsync(request.HostUserId, cancellationToken)
            ?? throw Validation("host", "The host account was not found.");
        if (host.Status != AccountStatus.Active)
        {
            throw new ForbiddenAppException("A deactivated account cannot host a lobby.");
        }

        // Distinct invitees, never the host (the host is added as the joined participant below).
        var inviteeIds = (request.InviteUserIds ?? [])
            .Where(id => id != request.HostUserId)
            .Distinct()
            .ToArray();

        if (!LobbyCapacity.CanAdmitAnother(format, inviteeIds.Length))
        {
            // 1 host + invitees must fit the format's maximum (1v1 ≤ 10, 2v2 ≤ 16).
            var (_, max) = LobbyCapacity.Bounds(format);
            throw Validation("invites", $"A {DraftFormats.ToWire(format)} lobby holds at most {max} participants including the host.");
        }

        var invitees = new List<User>(inviteeIds.Length);
        foreach (var inviteeId in inviteeIds)
        {
            var invitee = await identity.FindByIdAsync(inviteeId, cancellationToken)
                ?? throw Validation("invites", "One of the invited accounts no longer exists.");
            if (invitee.Status != AccountStatus.Active)
            {
                throw Validation("invites", $"{invitee.DisplayName} is deactivated and cannot be invited.");
            }

            invitees.Add(invitee);
        }

        var draft = Draft.Create(
            name: request.Name.Trim(),
            format: format,
            hostUserId: request.HostUserId,
            rosterTemplateId: template.Summary.Id,
            code: DraftCode.New());
        draft.OpenLobby(request.HostUserId);
        foreach (var invitee in invitees)
        {
            draft.InviteParticipant(invitee.Id, request.HostUserId);
        }

        await transaction.ExecuteAsync(async ct =>
        {
            await drafts.AddAsync(draft, ct);
            // Same transaction (PR-20): each initial invitee's notification + outbox email commit with
            // the lobby itself — a rejected creation notifies no one.
            foreach (var invitee in invitees)
            {
                await lifecycle.NotifyInvitedAsync(draft, invitee, ct);
            }

            await drafts.SaveChangesAsync(ct);
        }, cancellationToken);

        // §15 lobby-to-draft-start conversion (numerator lands in StartDraft): after commit only.
        (analytics ?? NullProductAnalytics.Instance).DraftCreated(DraftFormats.ToWire(format));

        return await LobbyProjection.ToDetailAsync(draft, identity, cancellationToken);
    }

    private static ValidationAppException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
