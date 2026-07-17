using FcDraft.Application.Features.Audit;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

/// <summary>
/// The admin Audit Log module's API (PR-21, §9.10): filtered, read-only queries over the two
/// append-only trails — draft events across all drafts, and the security/admin action trail. GET is
/// the ONLY verb this controller exposes: audit records cannot be created, edited, or deleted through
/// it (or any other normal API); draft corrections append compensating events via admin recovery.
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/audit")]
public sealed class AdminAuditController(ISender sender) : ControllerBase
{
    /// <summary>Draft lifecycle/pick events, filtered by draft, actor, type, and date; newest first.</summary>
    [HttpGet("draft-events")]
    public async Task<ActionResult<IReadOnlyList<DraftAuditEventDto>>> DraftEvents(
        [FromQuery] Guid? draftId,
        [FromQuery] string? type,
        [FromQuery] Guid? actorUserId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new QueryDraftEventsQuery(draftId, type, actorUserId, from, to, Math.Clamp(take, 1, 200)),
            cancellationToken));

    /// <summary>Security and admin audit events, filtered by action, user, email, and date; newest first.</summary>
    [HttpGet("security-events")]
    public async Task<ActionResult<IReadOnlyList<SecurityAuditEventDto>>> SecurityEvents(
        [FromQuery] string? action,
        [FromQuery] Guid? userId,
        [FromQuery] string? email,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new QuerySecurityEventsQuery(action, userId, email, from, to, Math.Clamp(take, 1, 200)),
            cancellationToken));
}
