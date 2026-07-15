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

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginBody body, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new LoginCommand(body.Email, body.Password), cancellationToken));

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
}
