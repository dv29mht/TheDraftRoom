using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using MediatR;

namespace FcDraft.Application.Features.Overview;

// The admin Overview dashboard (PRD §8.2, PR-24): a read-only user/draft/engagement summary plus
// alerts. Every figure is derived from the existing stores/readers so it works identically on both
// storage branches (in-memory foundation and EF/Postgres). The §15 product-analytics meter is
// write-only, so the engagement figures are re-derived from the append-only draft-event trail
// instead of read back from the meter.

/// <summary>User directory summary: total accounts and their activation state.</summary>
public sealed record AdminUserSummary(int Total, int Activated, int AwaitingActivation, int Invited);

/// <summary>Draft counts: headline rollups plus the full current-status breakdown.</summary>
public sealed record AdminDraftSummary(
    int Total,
    int Live,
    int Completed,
    int Cancelled,
    int OneVOne,
    int TwoVTwo,
    IReadOnlyDictionary<string, int> ByStatus);

/// <summary>§15-equivalent engagement figures, re-derived from the append-only draft-event trail.</summary>
public sealed record AdminEngagementSummary(
    int Created,
    int Started,
    int Completed,
    double LobbyToStartRate,
    double CompletionRate,
    int PicksAccepted,
    int AutoPicks,
    double AutoPickRate);

/// <summary>Whole-outbox delivery health.</summary>
public sealed record AdminEmailSummary(int Pending, int Sent, int Failed);

/// <summary>One attention item for the §8.2 "alerts" strip. Severity: info | warning.</summary>
public sealed record AdminAlertDto(string Severity, string Message);

/// <summary>The complete Overview snapshot.</summary>
public sealed record AdminOverviewDto(
    AdminUserSummary Users,
    AdminDraftSummary Drafts,
    AdminEngagementSummary Engagement,
    AdminEmailSummary Email,
    IReadOnlyList<AdminAlertDto> Alerts,
    DateTimeOffset GeneratedAt);

public sealed record GetAdminOverviewQuery : IRequest<AdminOverviewDto>;

public sealed class GetAdminOverviewQueryHandler(
    IIdentityService identity,
    IDraftStore drafts,
    IDraftEventReader events,
    IEmailOutboxReader outbox,
    TimeProvider clock)
    : IRequestHandler<GetAdminOverviewQuery, AdminOverviewDto>
{
    // Current statuses that count as an in-flight draft session (past start, not yet terminal).
    private static readonly HashSet<DraftStatus> LiveStatuses =
    [
        DraftStatus.SpinnerRanking,
        DraftStatus.ClubSelection,
        DraftStatus.PositionDraft,
        DraftStatus.Paused,
    ];

    public async Task<AdminOverviewDto> Handle(GetAdminOverviewQuery request, CancellationToken cancellationToken)
    {
        // Users: the directory-wide tallies ride on the paged search (independent of the page/filter).
        var directory = await identity.SearchUsersAsync(new UserDirectoryQuery(null, 1, 1), cancellationToken);
        var users = new AdminUserSummary(
            Total: directory.Total,
            Activated: directory.ActivatedCount,
            AwaitingActivation: Math.Max(0, directory.Total - directory.ActivatedCount),
            Invited: directory.InvitedCount);

        // Drafts: current-status distribution from the store (admin sees every draft).
        var allDrafts = await drafts.ListAsync(cancellationToken);
        var byStatus = allDrafts
            .GroupBy(draft => draft.Status)
            .ToDictionary(group => group.Key.ToString(), group => group.Count());
        var draftSummary = new AdminDraftSummary(
            Total: allDrafts.Count,
            Live: allDrafts.Count(draft => LiveStatuses.Contains(draft.Status)),
            Completed: allDrafts.Count(draft => draft.Status == DraftStatus.Completed),
            Cancelled: allDrafts.Count(draft =>
                draft.Status is DraftStatus.Cancelled or DraftStatus.Abandoned),
            OneVOne: allDrafts.Count(draft => draft.Format == DraftFormat.OneVsOne),
            TwoVTwo: allDrafts.Count(draft => draft.Format == DraftFormat.TwoVsTwo),
            ByStatus: byStatus);

        // Engagement: re-derived from the immutable event trail (the §15 meter is write-only).
        var eventCounts = await events.CountByTypeAsync(from: null, to: null, cancellationToken);
        int Count(DraftEventType type) => eventCounts.GetValueOrDefault(type.ToString(), 0);
        var created = Count(DraftEventType.DraftCreated);
        var started = Count(DraftEventType.DraftStarted);
        var completed = Count(DraftEventType.DraftCompleted);
        var autoPicks = Count(DraftEventType.PickAutoSelected);
        var picksAccepted = Count(DraftEventType.PickAccepted) + autoPicks;
        var engagement = new AdminEngagementSummary(
            Created: created,
            Started: started,
            Completed: completed,
            LobbyToStartRate: Rate(started, created),
            CompletionRate: Rate(completed, started),
            PicksAccepted: picksAccepted,
            AutoPicks: autoPicks,
            AutoPickRate: Rate(autoPicks, picksAccepted));

        var tallies = await outbox.GetStatusTalliesAsync(cancellationToken);
        var email = new AdminEmailSummary(tallies.Pending, tallies.Sent, tallies.Failed);

        var alerts = BuildAlerts(users, byStatus, email);

        return new AdminOverviewDto(users, draftSummary, engagement, email, alerts, clock.GetUtcNow());
    }

    // Rate as a fraction in [0,1]; 0 when the denominator is 0 (nothing to convert yet).
    private static double Rate(int numerator, int denominator) =>
        denominator <= 0 ? 0d : Math.Round((double)numerator / denominator, 4);

    private static IReadOnlyList<AdminAlertDto> BuildAlerts(
        AdminUserSummary users, IReadOnlyDictionary<string, int> byStatus, AdminEmailSummary email)
    {
        var alerts = new List<AdminAlertDto>();

        if (email.Failed > 0)
        {
            alerts.Add(new AdminAlertDto("warning",
                $"{email.Failed} email(s) failed to deliver — review the Communications outbox."));
        }

        var paused = byStatus.GetValueOrDefault(nameof(DraftStatus.Paused), 0);
        if (paused > 0)
        {
            alerts.Add(new AdminAlertDto("warning",
                $"{paused} draft(s) are paused and may need a host or admin to resume or cancel them."));
        }

        if (users.AwaitingActivation > 0)
        {
            alerts.Add(new AdminAlertDto("info",
                $"{users.AwaitingActivation} account(s) haven't set a password yet."));
        }

        return alerts;
    }
}
