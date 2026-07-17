using FcDraft.Application.Features.Overview;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

/// <summary>
/// The admin Overview dashboard's API (PRD §8.2, PR-24): a single read-only user/draft/engagement
/// summary plus alerts. GET only — it reports state, it never mutates. Works on both storage
/// branches because every figure is composed from the shared store/reader interfaces.
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/overview")]
public sealed class AdminOverviewController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdminOverviewDto>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAdminOverviewQuery(), cancellationToken));
}
