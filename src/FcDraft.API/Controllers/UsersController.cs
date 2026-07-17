using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Net.Mail;

namespace FcDraft.API.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/users")]
public sealed class UsersController(IIdentityService identity, ISecurityAuditService audit) : ControllerBase
{
    public sealed record CreateUserBody(string Email, string DisplayName);
    public sealed record UpdateUserBody(
        string DisplayName,
        string Email,
        string Role,
        string? AvatarUrl,
        string? PreferredTeamName);
    public sealed record UserDto(
        Guid Id,
        string DisplayName,
        string Email,
        string Role,
        string Status,
        bool MustChangePassword,
        string? AvatarUrl,
        string? PreferredTeamName,
        DateTimeOffset? InvitationSentAt,
        DateTimeOffset CreatedAt);
    public sealed record PagedUsersDto(
        IReadOnlyCollection<UserDto> Items,
        int Page,
        int PageSize,
        int Total,
        int TotalPages,
        int InvitedCount,
        int ActivatedCount);

    [HttpGet]
    public async Task<ActionResult<PagedUsersDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        // Filtering, counting, and paging run in the store (see IIdentityService.SearchUsersAsync);
        // the directory is never loaded whole into memory.
        var directory = await identity.SearchUsersAsync(
            new UserDirectoryQuery(search, page, pageSize),
            cancellationToken);

        return Ok(new PagedUsersDto(
            directory.Items.Select(ToDto).ToArray(),
            directory.Page,
            directory.PageSize,
            directory.Total,
            directory.TotalPages,
            directory.InvitedCount,
            directory.ActivatedCount));
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.DisplayName))
        {
            return ValidationProblem("A name is required.");
        }

        if (string.IsNullOrWhiteSpace(body.Email) || !MailAddress.TryCreate(body.Email, out _))
        {
            return ValidationProblem("A valid email address is required.");
        }

        var user = await identity.CreateUserAsync(body.DisplayName, body.Email, UserRole.Player, cancellationToken);
        await AuditAsync(SecurityAuditAction.UserCreated, $"Created player {user.Email}.", cancellationToken);
        return CreatedAtAction(nameof(List), new { id = user.Id }, ToDto(user));
    }

    [HttpPut("{userId:guid}")]
    public async Task<ActionResult<UserDto>> Update(
        Guid userId,
        UpdateUserBody body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.DisplayName))
        {
            return ValidationProblem("A display name is required.");
        }

        if (string.IsNullOrWhiteSpace(body.Email) || !MailAddress.TryCreate(body.Email, out _))
        {
            return ValidationProblem("A valid email address is required.");
        }

        if (!Enum.TryParse<UserRole>(body.Role, ignoreCase: true, out var role))
        {
            return ValidationProblem("A valid role is required.");
        }

        var target = await identity.FindByIdAsync(userId, cancellationToken);
        if (target is null) return NotFound();

        var currentEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.Equals(target.Email, currentEmail, StringComparison.OrdinalIgnoreCase)
            && target.Role == UserRole.Admin
            && role != UserRole.Admin)
        {
            return ValidationProblem("You cannot remove your own administrator role.");
        }

        var updated = await identity.UpdateUserAsync(
            userId,
            new UserProfileUpdate(body.DisplayName, body.Email, role, body.AvatarUrl, body.PreferredTeamName),
            cancellationToken);
        await AuditAsync(
            SecurityAuditAction.UserUpdated,
            $"Updated {updated.Email} (role {updated.Role.ToString().ToLowerInvariant()}).",
            cancellationToken);
        return Ok(ToDto(updated));
    }

    [HttpPost("{userId:guid}/deactivate")]
    public async Task<ActionResult<UserDto>> Deactivate(Guid userId, CancellationToken cancellationToken)
    {
        var target = await identity.FindByIdAsync(userId, cancellationToken);
        if (target is null) return NotFound();
        if (target.Role == UserRole.Admin)
        {
            return ValidationProblem("Administrator accounts cannot be deactivated.");
        }

        var updated = await identity.SetUserStatusAsync(userId, AccountStatus.Deactivated, cancellationToken);
        await AuditAsync(SecurityAuditAction.AccountDeactivated, $"Deactivated {updated.Email}.", cancellationToken);
        return Ok(ToDto(updated));
    }

    [HttpPost("{userId:guid}/activate")]
    public async Task<ActionResult<UserDto>> Activate(Guid userId, CancellationToken cancellationToken)
    {
        var target = await identity.FindByIdAsync(userId, cancellationToken);
        if (target is null) return NotFound();

        var updated = await identity.SetUserStatusAsync(userId, AccountStatus.Active, cancellationToken);
        await AuditAsync(SecurityAuditAction.AccountActivated, $"Activated {updated.Email}.", cancellationToken);
        return Ok(ToDto(updated));
    }

    [HttpPost("{userId:guid}/invite")]
    public async Task<ActionResult<UserDto>> SendInvitation(Guid userId, CancellationToken cancellationToken)
    {
        var invited = await identity.SendInvitationAsync(userId, cancellationToken);
        await AuditAsync(SecurityAuditAction.UserInvited, $"Sent an invitation to {invited.Email}.", cancellationToken);
        return Ok(ToDto(invited));
    }

    /// <summary>§9.10 (PR-21): every admin user change is attributable — actor id/email plus the target.</summary>
    private Task AuditAsync(SecurityAuditAction action, string detail, CancellationToken cancellationToken) =>
        audit.RecordAdminActionAsync(this, action, detail, cancellationToken);

    private static UserDto ToDto(User user) => new(
        user.Id,
        user.DisplayName,
        user.Email,
        user.Role.ToString().ToLowerInvariant(),
        user.Status.ToString().ToLowerInvariant(),
        user.MustChangePassword,
        user.AvatarUrl,
        user.PreferredTeamName,
        user.InvitationSentAt,
        user.CreatedAt);
}
