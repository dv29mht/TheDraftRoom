using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Rosters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

/// <summary>
/// Admin management of versioned roster templates (PR-09): list, inspect ordered slots, create a new
/// template, and activate one. A draft snapshots the active template at start (PR-10), so edits here
/// never affect an in-progress draft.
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/roster-templates")]
public sealed class AdminRosterTemplatesController(IRosterTemplateService templates) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RosterTemplateSummary>>> List(CancellationToken cancellationToken) =>
        Ok(await templates.ListAsync(cancellationToken));

    [HttpGet("active")]
    public async Task<ActionResult<RosterTemplateDetail>> Active(CancellationToken cancellationToken)
    {
        var active = await templates.GetActiveAsync(cancellationToken);
        return active is null ? NotFound() : Ok(active);
    }

    [HttpGet("{templateId:guid}")]
    public async Task<ActionResult<RosterTemplateDetail>> Get(Guid templateId, CancellationToken cancellationToken)
    {
        var detail = await templates.GetAsync(templateId, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost]
    public async Task<ActionResult<RosterTemplateSummary>> Create(
        CreateRosterTemplateRequest request, CancellationToken cancellationToken) =>
        Ok(await templates.CreateAsync(request, cancellationToken));

    [HttpPost("{templateId:guid}/activate")]
    public async Task<ActionResult<RosterTemplateSummary>> Activate(Guid templateId, CancellationToken cancellationToken)
    {
        if (await templates.GetAsync(templateId, cancellationToken) is null)
        {
            return NotFound();
        }

        return Ok(await templates.ActivateAsync(templateId, cancellationToken));
    }
}
