using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FcDraft.Application.Features.Auth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    public sealed record LoginBody(string Email, string Password);
    public sealed record ChangePasswordBody(string CurrentPassword, string NewPassword, string ConfirmPassword);
    public sealed record ForgotPasswordBody(string Email);
    public sealed record ResetPasswordBody(string Token, string NewPassword, string ConfirmPassword);

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new LoginCommand(body.Email, body.Password, ClientIp), cancellationToken));

    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult<AuthResponse>> ChangePassword(
        ChangePasswordBody body,
        CancellationToken cancellationToken)
    {
        var email = User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Email)
            ?? throw new UnauthorizedAccessException("The authenticated token has no email claim.");
        return Ok(await sender.Send(
            new ChangePasswordCommand(email, body.CurrentPassword, body.NewPassword, body.ConfirmPassword),
            cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordBody body, CancellationToken cancellationToken)
    {
        await sender.Send(new ForgotPasswordCommand(body.Email, ClientIp), cancellationToken);
        // Always 202: the response never reveals whether the email matched an account.
        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<ActionResult<AuthResponse>> ResetPassword(
        ResetPasswordBody body,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new ResetPasswordCommand(body.Token, body.NewPassword, body.ConfirmPassword),
            cancellationToken));

    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null || !Guid.TryParse(userId, out var id))
        {
            throw new UnauthorizedAccessException("The authenticated token has no subject claim.");
        }

        await sender.Send(new RevokeSessionsCommand(id), cancellationToken);
        return NoContent();
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
