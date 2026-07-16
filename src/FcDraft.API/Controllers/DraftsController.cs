using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Application.Features.Rosters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

/// <summary>
/// The draft lobby surface (PR-11): create a 1v1/2v2 lobby, reopen its authoritative snapshot, and drive
/// attendance (invite, join, host-only remove, and locking the lobby into team formation). Thin — every
/// business rule (capacity §6.2, host-only control, optimistic version conflicts, deactivated-user
/// rejection) lives in the MediatR handlers. Supersedes the legacy <c>/api/draft-rooms</c> stub.
/// </summary>
[ApiController]
[Authorize]
[Route("api/drafts")]
public sealed class DraftsController(
    ISender sender,
    IRosterTemplateService templates,
    IAdminNotificationService notifications) : ControllerBase
{
    public sealed record CreateDraftBody(string Name, string Format, Guid? RosterTemplateId, IReadOnlyList<Guid>? InviteUserIds);
    public sealed record InviteBody(Guid InviteUserId, int ExpectedVersion);
    public sealed record VersionBody(int ExpectedVersion);
    public sealed record AssignSeedBody(Guid ParticipantUserId, string? Seed, int ExpectedVersion);
    public sealed record FormTeamsBody(IReadOnlyList<TeamFormationInput>? Teams, int ExpectedVersion);
    public sealed record ReadyBody(bool Ready, int ExpectedVersion);

    /// <summary>Roster templates a host can choose from when creating a lobby.</summary>
    [HttpGet("roster-templates")]
    public async Task<ActionResult<IReadOnlyList<RosterTemplateSummary>>> RosterTemplates(CancellationToken cancellationToken) =>
        Ok(await templates.ListAsync(cancellationToken));

    /// <summary>Active accounts the caller can invite to a lobby.</summary>
    [HttpGet("invitable-users")]
    public async Task<ActionResult<IReadOnlyList<InvitableUserDto>>> InvitableUsers(
        [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListInvitableUsersQuery(CallerId, search), cancellationToken));

    /// <summary>The drafts the caller hosts or participates in (an admin sees them all).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DraftSummary>>> List(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListDraftsQuery(CallerId, CallerIsAdmin), cancellationToken));

    /// <summary>The authoritative lobby snapshot. Restricted to its participants, the host, and admins.</summary>
    [HttpGet("{draftId:guid}")]
    public async Task<ActionResult<DraftDetail>> Get(Guid draftId, CancellationToken cancellationToken)
    {
        var detail = await sender.Send(new GetDraftQuery(draftId), cancellationToken);
        if (detail is null || !CallerMaySee(detail))
        {
            // 404 rather than 403 for non-participants, so a lobby's existence is not leaked.
            return NotFound();
        }

        return Ok(detail);
    }

    [HttpPost]
    public async Task<ActionResult<DraftDetail>> Create(CreateDraftBody body, CancellationToken cancellationToken)
    {
        var detail = await sender.Send(
            new CreateDraftCommand(body.Name, body.Format, CallerId, body.RosterTemplateId, body.InviteUserIds),
            cancellationToken);
        notifications.Publish(
            "draft.created",
            "Draft lobby created",
            $"{detail.Summary.Name} ({detail.Summary.Format}) · {detail.Summary.Code}.");
        return CreatedAtAction(nameof(Get), new { draftId = detail.Summary.Id }, detail);
    }

    [HttpPost("{draftId:guid}/invite")]
    public async Task<ActionResult<DraftDetail>> Invite(Guid draftId, InviteBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new InviteParticipantCommand(draftId, body.InviteUserId, body.ExpectedVersion, CallerId, CallerIsAdmin),
            cancellationToken));

    [HttpPost("{draftId:guid}/join")]
    public async Task<ActionResult<DraftDetail>> Join(Guid draftId, VersionBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new JoinDraftCommand(draftId, body.ExpectedVersion, CallerId), cancellationToken));

    [HttpPost("{draftId:guid}/participants/{userId:guid}/remove")]
    public async Task<ActionResult<DraftDetail>> Remove(
        Guid draftId, Guid userId, VersionBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new RemoveParticipantCommand(draftId, userId, body.ExpectedVersion, CallerId, CallerIsAdmin),
            cancellationToken));

    [HttpPost("{draftId:guid}/lock")]
    public async Task<ActionResult<DraftDetail>> Lock(Guid draftId, VersionBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new LockLobbyCommand(draftId, body.ExpectedVersion, CallerId, CallerIsAdmin), cancellationToken));

    /// <summary>Assigns a participant's Seed 1/Seed 2 in a 2v2 draft (host-only, team formation).</summary>
    [HttpPost("{draftId:guid}/seeds")]
    public async Task<ActionResult<DraftDetail>> AssignSeed(Guid draftId, AssignSeedBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new AssignSeedCommand(draftId, body.ParticipantUserId, body.Seed, body.ExpectedVersion, CallerId, CallerIsAdmin),
            cancellationToken));

    /// <summary>Replaces the team layout (host-only; 1v1 auto-projects solo teams, 2v2 pairs seeds).</summary>
    [HttpPost("{draftId:guid}/teams")]
    public async Task<ActionResult<DraftDetail>> FormTeams(Guid draftId, FormTeamsBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new FormTeamsCommand(draftId, body.Teams, body.ExpectedVersion, CallerId, CallerIsAdmin),
            cancellationToken));

    /// <summary>Sets the calling participant's readiness (self-service).</summary>
    [HttpPost("{draftId:guid}/ready")]
    public async Task<ActionResult<DraftDetail>> Ready(Guid draftId, ReadyBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new SetReadyCommand(draftId, body.Ready, body.ExpectedVersion, CallerId), cancellationToken));

    /// <summary>Opens the ready check (host-only, TeamFormation → ReadyCheck).</summary>
    [HttpPost("{draftId:guid}/ready-check")]
    public async Task<ActionResult<DraftDetail>> BeginReadyCheck(Guid draftId, VersionBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new BeginReadyCheckCommand(draftId, body.ExpectedVersion, CallerId, CallerIsAdmin), cancellationToken));

    /// <summary>Reopens team formation to fix teams (host-only, ReadyCheck → TeamFormation).</summary>
    [HttpPost("{draftId:guid}/reopen-teams")]
    public async Task<ActionResult<DraftDetail>> ReopenTeams(Guid draftId, VersionBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ReopenTeamFormationCommand(draftId, body.ExpectedVersion, CallerId, CallerIsAdmin), cancellationToken));

    /// <summary>Starts the draft once attendance, teams, and readiness pass (host-only, ReadyCheck → SpinnerRanking).</summary>
    [HttpPost("{draftId:guid}/start")]
    public async Task<ActionResult<DraftDetail>> Start(Guid draftId, VersionBody body, CancellationToken cancellationToken)
    {
        await sender.Send(new StartDraftCommand(draftId, body.ExpectedVersion, CallerId, CallerIsAdmin), cancellationToken);
        return Ok(await sender.Send(new GetDraftQuery(draftId), cancellationToken));
    }

    /// <summary>Commits the server-authoritative spinner order (host-only, SpinnerRanking).</summary>
    [HttpPost("{draftId:guid}/spinner")]
    public async Task<ActionResult<DraftDetail>> Spinner(Guid draftId, VersionBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new CommitSpinnerCommand(draftId, body.ExpectedVersion, CallerId, CallerIsAdmin), cancellationToken));

    private bool CallerMaySee(DraftDetail detail) =>
        CallerIsAdmin
        || detail.Summary.HostUserId == CallerId
        || detail.Participants.Any(participant => participant.UserId == CallerId);

    private bool CallerIsAdmin => User.IsInRole("admin");

    private Guid CallerId =>
        Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var id)
            ? id
            : throw new UnauthorizedAccessException("The authenticated token has no subject claim.");
}
