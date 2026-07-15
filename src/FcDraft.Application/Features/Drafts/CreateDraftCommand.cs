using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// Creates a new draft in the <see cref="DraftStatus.Draft"/> state, hosted by <paramref name="HostUserId"/>,
/// binding the currently active roster template (snapshotted later at start). Appends the opening
/// <see cref="DraftEventType.DraftCreated"/> event. Lobby participants/invitations arrive in PR-11.
/// </summary>
public sealed record CreateDraftCommand(string Name, string Format, Guid HostUserId) : IRequest<DraftSummary>;

public sealed class CreateDraftCommandValidator : AbstractValidator<CreateDraftCommand>
{
    public CreateDraftCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Format)
            .Must(format => DraftFormats.TryParse(format, out _))
            .WithMessage("Format must be '1v1' or '2v2'.");
        RuleFor(command => command.HostUserId).NotEmpty();
    }
}

public sealed class CreateDraftCommandHandler(
    IDraftStore drafts,
    IRosterTemplateService templates,
    ITransactionRunner transaction)
    : IRequestHandler<CreateDraftCommand, DraftSummary>
{
    public async Task<DraftSummary> Handle(CreateDraftCommand request, CancellationToken cancellationToken)
    {
        DraftFormats.TryParse(request.Format, out var format);

        var template = await templates.GetActiveAsync(cancellationToken)
            ?? throw new ValidationAppException(new Dictionary<string, string[]>
            {
                ["rosterTemplate"] = ["No active roster template is configured; a draft cannot be created."],
            });

        var draft = Draft.Create(
            name: request.Name.Trim(),
            format: format,
            hostUserId: request.HostUserId,
            rosterTemplateId: template.Summary.Id,
            code: DraftCode.New());

        await transaction.ExecuteAsync(async ct =>
        {
            await drafts.AddAsync(draft, ct);
            await drafts.SaveChangesAsync(ct);
        }, cancellationToken);

        return DraftMapper.ToSummary(draft);
    }
}
