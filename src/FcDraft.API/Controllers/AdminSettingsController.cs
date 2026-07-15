using FcDraft.Infrastructure.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FcDraft.API.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/settings")]
public sealed class AdminSettingsController(
    IOptions<BrevoOptions> brevo,
    IWebHostEnvironment environment) : ControllerBase
{
    public sealed record SettingsStatusDto(
        string Environment,
        string Storage,
        bool EmailConfigured,
        string SenderName,
        string? SenderEmail,
        string LoginUrl);

    [HttpGet]
    public ActionResult<SettingsStatusDto> Get()
    {
        var options = brevo.Value;
        var emailConfigured = !string.IsNullOrWhiteSpace(options.ApiKey)
            && !string.IsNullOrWhiteSpace(options.SenderEmail);
        return Ok(new SettingsStatusDto(
            environment.EnvironmentName,
            "In-memory (non-persistent)",
            emailConfigured,
            options.SenderName,
            string.IsNullOrWhiteSpace(options.SenderEmail) ? null : options.SenderEmail,
            options.LoginUrl));
    }
}
