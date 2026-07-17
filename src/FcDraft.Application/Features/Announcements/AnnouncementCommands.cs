using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Announcements;

// Admin announcements (PR-21, §9.8): compose → preview → explicit confirmation → send. The preview
// resolves the audience and reports the §9.9 opt-out split; the confirmed send re-resolves it and
// REJECTS (409) when the count moved since the preview — the server-side proof that the §9.8
// confirmation step happened against the audience actually being addressed. Everything the send
// writes — the campaign record, every in-app notification, every outbox email row, and the
// AnnouncementSent audit record — commits inside ONE transaction; the emails themselves go through
// the durable outbox, throttled across delivery windows, so a Brevo outage never rolls anything back.

/// <summary>Wire values for the audience selection ("all" active players / "draft" participants).</summary>
public static class AnnouncementAudiences
{
    public const string All = "all";
    public const string Draft = "draft";

    /// <summary>The in-app notification type announcements create through the PR-20 pipeline.</summary>
    public const string NotificationType = "announcement";

    public static bool TryParse(string? value, out AnnouncementAudience audience)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case All:
                audience = AnnouncementAudience.AllPlayers;
                return true;
            case Draft:
                audience = AnnouncementAudience.DraftParticipants;
                return true;
            default:
                audience = default;
                return false;
        }
    }

    public static string ToWire(AnnouncementAudience audience) =>
        audience == AnnouncementAudience.DraftParticipants ? Draft : All;
}

/// <summary>What the admin reviews before confirming: the resolved audience and its opt-out split.</summary>
public sealed record AnnouncementPreviewDto(
    string Subject,
    string Body,
    string Audience,
    Guid? DraftId,
    string? DraftName,
    string AudienceLabel,
    int RecipientCount,
    int EmailRecipientCount,
    int OptedOutCount);

/// <summary>One campaign record plus its live outbox delivery tallies (§9.8 delivery visibility).</summary>
public sealed record AnnouncementDto(
    Guid Id,
    string Subject,
    string Body,
    string Audience,
    Guid? DraftId,
    string AudienceLabel,
    int RecipientCount,
    int EmailCount,
    int OptedOutCount,
    Guid RequestedByUserId,
    string RequestedByEmail,
    DateTimeOffset RequestedAt,
    int EmailsPending,
    int EmailsSent,
    int EmailsFailed);

/// <summary>Resolves and previews the audience without sending anything.</summary>
public sealed record PreviewAnnouncementQuery(
    string Subject, string Body, string Audience, Guid? DraftId)
    : IRequest<AnnouncementPreviewDto>;

/// <summary>
/// The confirmed send. <see cref="ConfirmedRecipientCount"/> is the count the admin saw on the
/// preview — a mismatch with the freshly resolved audience is a 409, exactly like a stale draft
/// version, so a send can never quietly address an audience the admin did not review.
/// </summary>
public sealed record SendAnnouncementCommand(
    string Subject, string Body, string Audience, Guid? DraftId,
    int ConfirmedRecipientCount, Guid ActorUserId, string ActorEmail)
    : IRequest<AnnouncementDto>;

/// <summary>The most recent campaigns with delivery tallies, newest first.</summary>
public sealed record ListAnnouncementsQuery(int Take = 50) : IRequest<IReadOnlyList<AnnouncementDto>>;

public sealed class PreviewAnnouncementQueryValidator : AbstractValidator<PreviewAnnouncementQuery>
{
    public PreviewAnnouncementQueryValidator()
    {
        RuleFor(query => query.Subject).NotEmpty().MaximumLength(AnnouncementRules.SubjectMaxLength);
        RuleFor(query => query.Body).NotEmpty().MaximumLength(AnnouncementRules.BodyMaxLength);
        RuleFor(query => query.Audience)
            .Must(audience => AnnouncementAudiences.TryParse(audience, out _))
            .WithMessage("The audience must be 'all' (active players) or 'draft' (participants of a draft).");
        RuleFor(query => query.DraftId)
            .NotNull()
            .When(query => string.Equals(query.Audience?.Trim(), AnnouncementAudiences.Draft, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Choose the draft whose participants should receive this announcement.");
    }
}

public sealed class SendAnnouncementCommandValidator : AbstractValidator<SendAnnouncementCommand>
{
    public SendAnnouncementCommandValidator()
    {
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(AnnouncementRules.SubjectMaxLength);
        RuleFor(command => command.Body).NotEmpty().MaximumLength(AnnouncementRules.BodyMaxLength);
        RuleFor(command => command.Audience)
            .Must(audience => AnnouncementAudiences.TryParse(audience, out _))
            .WithMessage("The audience must be 'all' (active players) or 'draft' (participants of a draft).");
        RuleFor(command => command.DraftId)
            .NotNull()
            .When(command => string.Equals(command.Audience?.Trim(), AnnouncementAudiences.Draft, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Choose the draft whose participants should receive this announcement.");
        // A send is only valid against a reviewed, non-empty audience: the preview step supplies this count.
        RuleFor(command => command.ConfirmedRecipientCount).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.ActorEmail).NotEmpty();
    }
}

public sealed class ListAnnouncementsQueryValidator : AbstractValidator<ListAnnouncementsQuery>
{
    public ListAnnouncementsQueryValidator()
    {
        RuleFor(query => query.Take).InclusiveBetween(1, 200);
    }
}

/// <summary>Shared limits. The body cap keeps the outbox payload JSON within its column.</summary>
public static class AnnouncementRules
{
    public const int SubjectMaxLength = 160;
    public const int BodyMaxLength = 2000;

    /// <summary>
    /// The §9.8 bulk-send throttle: announcement emails are enqueued in batches of
    /// <see cref="EmailBatchSize"/>, each batch deliverable one <see cref="EmailBatchInterval"/> later
    /// than the previous — matching the outbox worker's own cadence (20 rows per 15 s poll), so a large
    /// audience drains steadily instead of bursting at Brevo.
    /// </summary>
    public const int EmailBatchSize = 20;

    public static readonly TimeSpan EmailBatchInterval = TimeSpan.FromSeconds(15);
}

/// <summary>The resolved audience: active accounts only (§18 — active-user audience, never a scraped list).</summary>
public sealed record ResolvedAudience(
    IReadOnlyList<User> Recipients, string Label, Guid? DraftId, string? DraftName);

/// <summary>
/// Resolves the audience selection to concrete active accounts. Shared by preview and send so the
/// confirmed-count check compares like with like.
/// </summary>
public static class AnnouncementAudienceResolver
{
    private const int DirectoryPageSize = 100;

    public static async Task<ResolvedAudience> ResolveAsync(
        string audienceValue, Guid? draftId,
        IIdentityService identity, IDraftStore drafts,
        CancellationToken cancellationToken)
    {
        if (!AnnouncementAudiences.TryParse(audienceValue, out var audience))
        {
            throw new ValidationAppException(new Dictionary<string, string[]>
            {
                ["audience"] = ["The audience must be 'all' or 'draft'."],
            });
        }

        if (audience == AnnouncementAudience.DraftParticipants)
        {
            var draft = await drafts.FindAsync(
                draftId ?? throw new KeyNotFoundException("Draft not found."), cancellationToken)
                ?? throw new KeyNotFoundException("Draft not found.");

            var recipients = new List<User>();
            foreach (var userId in draft.Participants.Select(participant => participant.UserId).Distinct())
            {
                var user = await identity.FindByIdAsync(userId, cancellationToken);
                if (user is not null && user.Status == AccountStatus.Active)
                {
                    recipients.Add(user);
                }
            }

            return new ResolvedAudience(recipients, $"Participants of “{draft.Name}”", draft.Id, draft.Name);
        }

        // All active registered players (§9.8): page through the directory so a large roster is never
        // silently truncated — the same pattern as the lobby's invitable-users query.
        var all = new List<User>();
        var page = 1;
        while (true)
        {
            var directory = await identity.SearchUsersAsync(
                new UserDirectoryQuery(null, page, DirectoryPageSize), cancellationToken);
            all.AddRange(directory.Items.Where(user => user.Status == AccountStatus.Active));
            if (page >= directory.TotalPages || directory.Items.Count == 0)
            {
                break;
            }

            page++;
        }

        return new ResolvedAudience(all, "All active players", null, null);
    }
}

public sealed class PreviewAnnouncementQueryHandler(
    IIdentityService identity, IDraftStore drafts)
    : IRequestHandler<PreviewAnnouncementQuery, AnnouncementPreviewDto>
{
    public async Task<AnnouncementPreviewDto> Handle(
        PreviewAnnouncementQuery request, CancellationToken cancellationToken)
    {
        var audience = await AnnouncementAudienceResolver.ResolveAsync(
            request.Audience, request.DraftId, identity, drafts, cancellationToken);

        var optedOut = audience.Recipients.Count(user => user.OptionalEmailOptOut);
        return new AnnouncementPreviewDto(
            request.Subject.Trim(),
            request.Body.Trim(),
            AnnouncementAudiences.ToWire(
                AnnouncementAudiences.TryParse(request.Audience, out var parsed) ? parsed : default),
            audience.DraftId,
            audience.DraftName,
            audience.Label,
            audience.Recipients.Count,
            audience.Recipients.Count - optedOut,
            optedOut);
    }
}

public sealed class SendAnnouncementCommandHandler(
    IIdentityService identity,
    IDraftStore drafts,
    IAnnouncementStore announcements,
    IUserNotificationStore notifications,
    IEmailQueue emails,
    IEmailOutboxReader outbox,
    ISecurityAuditService audit,
    ITransactionRunner transaction,
    TimeProvider clock)
    : IRequestHandler<SendAnnouncementCommand, AnnouncementDto>
{
    /// <summary>The in-app notification body cap (the user_notifications column is 1024).</summary>
    private const int NotificationBodyMaxLength = 1000;

    public async Task<AnnouncementDto> Handle(SendAnnouncementCommand request, CancellationToken cancellationToken)
    {
        var subject = request.Subject.Trim();
        var body = request.Body.Trim();

        var announcement = await transaction.ExecuteAsync(async ct =>
        {
            var audience = await AnnouncementAudienceResolver.ResolveAsync(
                request.Audience, request.DraftId, identity, drafts, ct);

            // The §9.8 confirmation gate: the admin confirmed a previewed audience COUNT; if the
            // directory or lobby moved since, this send addresses people the admin never reviewed.
            if (audience.Recipients.Count != request.ConfirmedRecipientCount)
            {
                throw new ConflictAppException(
                    $"The audience changed since the preview (it now has {audience.Recipients.Count} " +
                    $"recipients, not {request.ConfirmedRecipientCount}). Review the preview and confirm again.");
            }

            var now = clock.GetUtcNow();
            var optedOut = audience.Recipients.Count(user => user.OptionalEmailOptOut);
            var record = new Announcement
            {
                Subject = subject,
                Body = body,
                Audience = AnnouncementAudiences.TryParse(request.Audience, out var parsed)
                    ? parsed
                    : AnnouncementAudience.AllPlayers,
                DraftId = audience.DraftId,
                AudienceLabel = audience.Label,
                RecipientCount = audience.Recipients.Count,
                EmailCount = audience.Recipients.Count - optedOut,
                OptedOutCount = optedOut,
                RequestedByUserId = request.ActorUserId,
                RequestedByEmail = request.ActorEmail,
                RequestedAt = now,
            };
            await announcements.AddAsync(record, ct);

            var payload = new AnnouncementEmailPayload(record.Id, subject, body, audience.DraftId, audience.DraftName);
            var emailIndex = 0;
            foreach (var user in audience.Recipients)
            {
                // The matching in-app notification (PR-20 pipeline) always lands, opt-out or not.
                await notifications.AddAsync(new UserNotification
                {
                    UserId = user.Id,
                    Type = AnnouncementAudiences.NotificationType,
                    Title = subject,
                    Body = body.Length <= NotificationBodyMaxLength
                        ? body
                        : body[..NotificationBodyMaxLength] + "…",
                    DraftId = audience.DraftId,
                }, ct);

                // Announcements are the OPTIONAL email class (§9.9): the opt-out suppresses the email only.
                if (user.OptionalEmailOptOut)
                {
                    continue;
                }

                // The throttle: batch N delivers no earlier than N windows from now (§18 rate controls).
                var notBefore = now + AnnouncementRules.EmailBatchInterval * (emailIndex / AnnouncementRules.EmailBatchSize);
                await emails.EnqueueAnnouncementAsync(user.Email, user.DisplayName, payload, notBefore, ct);
                emailIndex++;
            }

            // §9.10: the bulk email request itself is an audited admin action, in the same transaction.
            await audit.RecordAsync(new SecurityAuditEntry(
                SecurityAuditAction.AnnouncementSent,
                UserId: request.ActorUserId,
                Email: request.ActorEmail,
                Detail: Truncate(
                    $"“{subject}” to {audience.Label}: {record.RecipientCount} recipients, " +
                    $"{record.EmailCount} emails, {record.OptedOutCount} opted out.", 512)), ct);

            await notifications.SaveChangesAsync(ct);
            await announcements.SaveChangesAsync(ct);
            return record;
        }, cancellationToken);

        // Post-commit: the response reflects the real outbox state (all pending on the durable branch;
        // already sent/failed on the in-memory branch, which delivers inline).
        var delivery = (await outbox.GetCampaignDeliveryAsync([announcement.Id], cancellationToken))
            .FirstOrDefault();
        return AnnouncementProjection.ToDto(announcement, delivery);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}

public sealed class ListAnnouncementsQueryHandler(
    IAnnouncementStore announcements, IEmailOutboxReader outbox)
    : IRequestHandler<ListAnnouncementsQuery, IReadOnlyList<AnnouncementDto>>
{
    public async Task<IReadOnlyList<AnnouncementDto>> Handle(
        ListAnnouncementsQuery request, CancellationToken cancellationToken)
    {
        var records = await announcements.ListRecentAsync(request.Take, cancellationToken);
        var delivery = await outbox.GetCampaignDeliveryAsync(
            records.Select(record => record.Id).ToArray(), cancellationToken);
        var byCampaign = delivery.ToDictionary(summary => summary.CampaignId);

        return records
            .Select(record => AnnouncementProjection.ToDto(
                record, byCampaign.TryGetValue(record.Id, out var tallies) ? tallies : null))
            .ToArray();
    }
}

internal static class AnnouncementProjection
{
    public static AnnouncementDto ToDto(Announcement record, CampaignDeliverySummary? delivery) => new(
        record.Id,
        record.Subject,
        record.Body,
        AnnouncementAudiences.ToWire(record.Audience),
        record.DraftId,
        record.AudienceLabel,
        record.RecipientCount,
        record.EmailCount,
        record.OptedOutCount,
        record.RequestedByUserId,
        record.RequestedByEmail,
        record.RequestedAt,
        delivery?.Pending ?? 0,
        delivery?.Sent ?? 0,
        delivery?.Failed ?? 0);
}
