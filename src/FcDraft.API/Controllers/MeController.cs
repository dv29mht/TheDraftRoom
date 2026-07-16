using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

/// <summary>
/// The caller's own notification centre and email preferences (PR-20, §9.9). Everything here is scoped
/// to the authenticated user — ids come from the token, never the request — so one user's notifications
/// are invisible (404) to another. Distinct from the admin-only live activity centre at
/// <c>/api/notifications</c>, which remains ephemeral and admin-scoped.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me")]
public sealed class MeController(ISender sender, IIdentityService identity) : ControllerBase
{
    public sealed record EmailPreferences(bool OptionalEmailOptOut);

    /// <summary>The caller's notifications (newest first) plus the unread badge count.</summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<UserNotificationsDto>> Notifications(
        [FromQuery] bool unreadOnly, [FromQuery] int take, CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new ListMyNotificationsQuery(CallerId, unreadOnly, take is >= 1 and <= 200 ? take : 50), cancellationToken));

    /// <summary>Marks one of the caller's notifications read. Another user's id is a 404.</summary>
    [HttpPost("notifications/{notificationId:guid}/read")]
    public async Task<ActionResult<UserNotificationsDto>> MarkRead(Guid notificationId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new MarkNotificationReadCommand(notificationId, CallerId), cancellationToken));

    /// <summary>Marks every unread notification of the caller read.</summary>
    [HttpPost("notifications/read-all")]
    public async Task<ActionResult<UserNotificationsDto>> MarkAllRead(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new MarkAllNotificationsReadCommand(CallerId), cancellationToken));

    /// <summary>The caller's §9.9 email preference.</summary>
    [HttpGet("email-preferences")]
    public async Task<ActionResult<EmailPreferences>> GetEmailPreferences(CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(CallerId, cancellationToken);
        return user is null ? NotFound() : Ok(new EmailPreferences(user.OptionalEmailOptOut));
    }

    /// <summary>
    /// Updates the caller's §9.9 email preference. Only OPTIONAL announcement-style emails are affected;
    /// security and essential service messages remain mandatory (enforced where sends are enqueued).
    /// </summary>
    [HttpPut("email-preferences")]
    public async Task<ActionResult<EmailPreferences>> SetEmailPreferences(
        EmailPreferences body, CancellationToken cancellationToken)
    {
        var user = await identity.SetOptionalEmailOptOutAsync(CallerId, body.OptionalEmailOptOut, cancellationToken);
        return Ok(new EmailPreferences(user.OptionalEmailOptOut));
    }

    private Guid CallerId =>
        Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var id)
            ? id
            : throw new UnauthorizedAccessException("The authenticated token has no subject claim.");
}
