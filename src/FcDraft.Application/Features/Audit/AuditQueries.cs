using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Audit;

// The PR-21 admin audit views (§9.10, §17.8): filtered, read-only queries over the two append-only
// trails — the per-draft DraftEvent history and the security/admin SecurityAuditEvent trail. There is
// deliberately NO command counterpart in this feature: audit records cannot be edited or deleted
// through any normal API; corrections happen by appending compensating events (admin recovery).

/// <summary>One immutable draft event, enriched with the actor's display name for attribution.</summary>
public sealed record DraftAuditEventDto(
    Guid DraftId,
    string DraftName,
    string DraftCode,
    int Sequence,
    string Type,
    string? FromStatus,
    string? ToStatus,
    int Version,
    Guid? ActorUserId,
    string? ActorName,
    string? Reason,
    DateTimeOffset CreatedAt);

/// <summary>One immutable security/admin audit record.</summary>
public sealed record SecurityAuditEventDto(
    Guid Id,
    string Action,
    Guid? UserId,
    string? Email,
    string? Detail,
    string? IpAddress,
    DateTimeOffset CreatedAt);

/// <summary>Draft events across all drafts, filtered by draft, user, type, and date; newest first.</summary>
public sealed record QueryDraftEventsQuery(
    Guid? DraftId = null,
    string? Type = null,
    Guid? ActorUserId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Take = 100)
    : IRequest<IReadOnlyList<DraftAuditEventDto>>;

/// <summary>Security/admin audit events, filtered by action, user, email, and date; newest first.</summary>
public sealed record QuerySecurityEventsQuery(
    string? Action = null,
    Guid? UserId = null,
    string? Email = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Take = 100)
    : IRequest<IReadOnlyList<SecurityAuditEventDto>>;

public sealed class QueryDraftEventsQueryValidator : AbstractValidator<QueryDraftEventsQuery>
{
    public QueryDraftEventsQueryValidator()
    {
        RuleFor(query => query.Take).InclusiveBetween(1, 200);
        RuleFor(query => query.Type)
            .Must(type => Enum.TryParse<DraftEventType>(type, ignoreCase: true, out _))
            .When(query => !string.IsNullOrWhiteSpace(query.Type))
            .WithMessage("Unknown draft event type.");
    }
}

public sealed class QuerySecurityEventsQueryValidator : AbstractValidator<QuerySecurityEventsQuery>
{
    public QuerySecurityEventsQueryValidator()
    {
        RuleFor(query => query.Take).InclusiveBetween(1, 200);
        RuleFor(query => query.Action)
            .Must(action => Enum.TryParse<SecurityAuditAction>(action, ignoreCase: true, out _))
            .When(query => !string.IsNullOrWhiteSpace(query.Action))
            .WithMessage("Unknown audit action.");
    }
}

public sealed class QueryDraftEventsQueryHandler(
    IDraftEventReader events, IIdentityService identity)
    : IRequestHandler<QueryDraftEventsQuery, IReadOnlyList<DraftAuditEventDto>>
{
    public async Task<IReadOnlyList<DraftAuditEventDto>> Handle(
        QueryDraftEventsQuery request, CancellationToken cancellationToken)
    {
        DraftEventType? type = null;
        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            if (!Enum.TryParse<DraftEventType>(request.Type, ignoreCase: true, out var parsed))
            {
                throw new ValidationAppException(new Dictionary<string, string[]>
                {
                    ["type"] = ["Unknown draft event type."],
                });
            }

            type = parsed;
        }

        var records = await events.QueryAsync(
            new DraftEventQuery(request.DraftId, type, request.ActorUserId, request.From, request.To, request.Take),
            cancellationToken);

        // Attribution (§17.8): resolve each distinct actor once. A null actor is the system (timer sweep).
        var actorNames = new Dictionary<Guid, string?>();
        foreach (var actorId in records.Where(record => record.ActorUserId.HasValue)
                     .Select(record => record.ActorUserId!.Value).Distinct())
        {
            var user = await identity.FindByIdAsync(actorId, cancellationToken);
            actorNames[actorId] = user?.DisplayName;
        }

        return records
            .Select(record => new DraftAuditEventDto(
                record.DraftId,
                record.DraftName,
                record.DraftCode,
                record.Sequence,
                record.Type,
                record.FromStatus,
                record.ToStatus,
                record.Version,
                record.ActorUserId,
                record.ActorUserId.HasValue
                    ? actorNames.GetValueOrDefault(record.ActorUserId.Value)
                    : null,
                record.Reason,
                record.CreatedAt))
            .ToArray();
    }
}

public sealed class QuerySecurityEventsQueryHandler(ISecurityAuditService audit)
    : IRequestHandler<QuerySecurityEventsQuery, IReadOnlyList<SecurityAuditEventDto>>
{
    public async Task<IReadOnlyList<SecurityAuditEventDto>> Handle(
        QuerySecurityEventsQuery request, CancellationToken cancellationToken)
    {
        SecurityAuditAction? action = null;
        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            if (!Enum.TryParse<SecurityAuditAction>(request.Action, ignoreCase: true, out var parsed))
            {
                throw new ValidationAppException(new Dictionary<string, string[]>
                {
                    ["action"] = ["Unknown audit action."],
                });
            }

            action = parsed;
        }

        var records = await audit.QueryAsync(
            new SecurityAuditQuery(action, request.UserId, request.Email, request.From, request.To, request.Take),
            cancellationToken);

        return records
            .Select(record => new SecurityAuditEventDto(
                record.Id,
                record.Action.ToString(),
                record.UserId,
                record.Email,
                record.Detail,
                record.IpAddress,
                record.CreatedAt))
            .ToArray();
    }
}
