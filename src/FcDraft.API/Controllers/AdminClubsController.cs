using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Rosters;
using FcDraft.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

/// <summary>
/// Admin curation of eligible five-star Kick Off clubs (PR-09). Clubs come from the active dataset;
/// star ratings are not in the source feed, so eligibility is set here. Only eligible clubs from the
/// active dataset are offered to draft flows.
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/clubs")]
public sealed class AdminClubsController(
    IClubDirectoryService clubs, ISecurityAuditService audit) : ControllerBase
{
    public sealed record FiveStarBody(bool Eligible);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClubDto>>> List(
        [FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await clubs.ListAsync(search, cancellationToken));

    [HttpGet("eligible")]
    public async Task<ActionResult<IReadOnlyList<ClubDto>>> Eligible(CancellationToken cancellationToken) =>
        Ok(await clubs.ListEligibleAsync(cancellationToken));

    [HttpPut("{clubId:guid}/five-star")]
    public async Task<ActionResult<ClubDto>> SetFiveStar(
        Guid clubId, FiveStarBody body, CancellationToken cancellationToken)
    {
        var club = await clubs.SetFiveStarEligibilityAsync(clubId, body.Eligible, cancellationToken);
        // §9.10 (PR-21): five-star curation is an audited admin action.
        await audit.RecordAdminActionAsync(
            this, SecurityAuditAction.ClubFiveStarChanged,
            $"{(body.Eligible ? "Marked" : "Unmarked")} “{club.Name}” as a five-star club.", cancellationToken);
        return Ok(club);
    }
}
