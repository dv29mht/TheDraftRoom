using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Rosters;
using FcDraft.Domain.Entities;
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
public sealed class AdminRosterTemplatesController(
    IRosterTemplateService templates, ISecurityAuditService audit) : ControllerBase
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
        CreateRosterTemplateRequest request, CancellationToken cancellationToken)
    {
        var created = await templates.CreateAsync(request, cancellationToken);
        // §9.10 (PR-21): template changes are audited admin actions.
        await audit.RecordAdminActionAsync(
            this, SecurityAuditAction.TemplateCreated, $"Created roster template “{created.Name}”.", cancellationToken);
        return Ok(created);
    }

    [HttpPost("{templateId:guid}/activate")]
    public async Task<ActionResult<RosterTemplateSummary>> Activate(Guid templateId, CancellationToken cancellationToken)
    {
        if (await templates.GetAsync(templateId, cancellationToken) is null)
        {
            return NotFound();
        }

        var activated = await templates.ActivateAsync(templateId, cancellationToken);
        await audit.RecordAdminActionAsync(
            this, SecurityAuditAction.TemplateActivated, $"Activated roster template “{activated.Name}”.", cancellationToken);
        return Ok(activated);
    }
}
