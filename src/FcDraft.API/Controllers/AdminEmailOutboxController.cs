using FcDraft.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

/// <summary>
/// Admin view of transactional email delivery status. Returns delivery metadata only (kind,
/// recipient, status, attempts, last error, timestamps) — never the one-time password or reset
/// token an outbox row carries.
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/email-outbox")]
public sealed class AdminEmailOutboxController(IEmailOutboxReader outbox) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmailOutboxStatusView>>> List(
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        return Ok(await outbox.GetRecentAsync(take, cancellationToken));
    }
}
