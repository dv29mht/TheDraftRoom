using FcDraft.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Middleware;

/// <summary>
/// Enforces the forced-password-change boundary server-side (PR-05 / PRD §12.3): a token minted for
/// an account that still must change its password may reach only the change-password endpoint. Every
/// other authenticated <c>/api</c> route is refused with 403, so the requirement does not rely on the
/// client honouring it.
/// </summary>
public sealed class ForcedPasswordChangeMiddleware(RequestDelegate next)
{
    // The only authenticated endpoint a must-change account may call.
    private static readonly PathString ChangePassword = new("/api/auth/change-password");

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && string.Equals(context.User.FindFirst(DraftClaimTypes.PasswordChangeRequired)?.Value, "true", StringComparison.OrdinalIgnoreCase)
            && context.Request.Path.StartsWithSegments("/api")
            && !context.Request.Path.StartsWithSegments(ChangePassword))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Password change required",
                Detail = "Set a new password before using the rest of the app.",
            });
            return;
        }

        await next(context);
    }
}
