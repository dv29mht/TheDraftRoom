using System.Text.Json;
using FcDraft.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/notifications")]
public sealed class NotificationsController(IAdminNotificationService notifications) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    [HttpGet]
    public ActionResult<IReadOnlyCollection<AdminNotification>> List() => Ok(notifications.Recent());

    [HttpGet("stream")]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.ContentType = "text/event-stream";
        await Response.WriteAsync(": connected\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);

        try
        {
            await foreach (var notification in notifications.SubscribeAsync(cancellationToken))
            {
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(notification, JsonOptions)}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
