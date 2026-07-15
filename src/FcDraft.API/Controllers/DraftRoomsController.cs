using System.Security.Claims;
using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

[ApiController]
[Authorize]
[Route("api/draft-rooms")]
public sealed class DraftRoomsController(
    IDraftRoomService rooms,
    IIdentityService identity,
    IAdminNotificationService notifications) : ControllerBase
{
    public sealed record CreateRoomBody(string Name, string Format);

    [HttpGet]
    public ActionResult<IReadOnlyCollection<DraftRoom>> List() => Ok(rooms.List());

    [HttpPost]
    public async Task<ActionResult<DraftRoom>> Create(CreateRoomBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || body.Format is not ("1v1" or "2v2"))
        {
            return ValidationProblem("A room name and valid format are required.");
        }

        var email = User.FindFirstValue(ClaimTypes.Email)
            ?? throw new UnauthorizedAccessException("The token has no email claim.");
        var host = await identity.FindByEmailAsync(email, cancellationToken)
            ?? throw new UnauthorizedAccessException("The host account was not found.");
        if (host.Status != AccountStatus.Active)
        {
            throw new ForbiddenAppException("This account has been deactivated and cannot create or join draft rooms.");
        }

        var room = rooms.Create(body.Name, body.Format, host);
        notifications.Publish(
            "room.created",
            "Draft room created",
            $"{room.HostName} created {room.Name} ({room.Format}) · {room.Code}.");
        return CreatedAtAction(nameof(List), new { id = room.Id }, room);
    }
}
