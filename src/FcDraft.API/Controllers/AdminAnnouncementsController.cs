using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FcDraft.Application.Features.Announcements;
using FcDraft.Infrastructure.Email;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FcDraft.API.Controllers;

/// <summary>
/// The admin Communications module's API (PR-21, §9.8): preview an announcement against its resolved
/// audience, send it after explicit confirmation, and list past campaigns with delivery tallies. Thin —
/// audience resolution, the §9.9 opt-out split, throttling, and the confirmed-count 409 live in the
/// MediatR handlers. Deliberately read+append only: no update or delete route exists, so the campaign
/// trail cannot be edited through any normal API.
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/announcements")]
public sealed class AdminAnnouncementsController(
    ISender sender,
    IOptions<BrevoOptions> brevo) : ControllerBase
{
    public sealed record ComposeBody(string Subject, string Body, string Audience, Guid? DraftId);

    public sealed record SendBody(
        string Subject, string Body, string Audience, Guid? DraftId, int ConfirmedRecipientCount);

    /// <summary>The §9.8 preview: subject, sender, audience count, and body — reviewed before any send.</summary>
    public sealed record PreviewResponse(
        AnnouncementPreviewDto Preview,
        string SenderName,
        string? SenderEmail,
        bool EmailConfigured);

    [HttpPost("preview")]
    public async Task<ActionResult<PreviewResponse>> Preview(ComposeBody body, CancellationToken cancellationToken)
    {
        var preview = await sender.Send(
            new PreviewAnnouncementQuery(body.Subject, body.Body, body.Audience, body.DraftId),
            cancellationToken);

        var options = brevo.Value;
        var emailConfigured = !string.IsNullOrWhiteSpace(options.ApiKey)
            && !string.IsNullOrWhiteSpace(options.SenderEmail);
        return Ok(new PreviewResponse(
            preview,
            options.SenderName,
            string.IsNullOrWhiteSpace(options.SenderEmail) ? null : options.SenderEmail,
            emailConfigured));
    }

    /// <summary>The confirmed send. A stale confirmed count (the audience moved since preview) is a 409.</summary>
    [HttpPost]
    public async Task<ActionResult<AnnouncementDto>> Send(SendBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new SendAnnouncementCommand(
                body.Subject, body.Body, body.Audience, body.DraftId,
                body.ConfirmedRecipientCount, CallerId, CallerEmail),
            cancellationToken));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AnnouncementDto>>> List(
        [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        return Ok(await sender.Send(new ListAnnouncementsQuery(take), cancellationToken));
    }

    private Guid CallerId =>
        Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var id)
            ? id
            : throw new UnauthorizedAccessException("The authenticated token has no subject claim.");

    private string CallerEmail =>
        User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Email)
        ?? string.Empty;
}
