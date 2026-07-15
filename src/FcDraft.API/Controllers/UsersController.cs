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
public sealed class UsersController(IIdentityService identity) : ControllerBase
{
    public sealed record CreateUserBody(string Email, string DisplayName);
    public sealed record UpdateUserBody(string DisplayName, string Email, string Role);
    public sealed record UserDto(
        Guid Id,
        string DisplayName,
        string Email,
        string Role,
        string Status,
        bool MustChangePassword,
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
        var users = await identity.ListUsersAsync(cancellationToken);
        pageSize = pageSize is 10 or 25 or 50 ? pageSize : 10;
        page = Math.Max(1, page);
        var query = users.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(user =>
                user.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || user.Email.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = query.ToArray();
        var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Length / (double)pageSize));
        page = Math.Min(page, totalPages);
        return Ok(new PagedUsersDto(
            filtered.Skip((page - 1) * pageSize).Take(pageSize).Select(ToDto).ToArray(),
            page,
            pageSize,
            filtered.Length,
            totalPages,
            users.Count(user => user.InvitationSentAt is not null),
            users.Count(user => !user.MustChangePassword)));
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

        var users = await identity.ListUsersAsync(cancellationToken);
        var target = users.FirstOrDefault(user => user.Id == userId);
        if (target is null) return NotFound();

        var currentEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.Equals(target.Email, currentEmail, StringComparison.OrdinalIgnoreCase)
            && target.Role == UserRole.Admin
            && role != UserRole.Admin)
        {
            return ValidationProblem("You cannot remove your own administrator role.");
        }

        var updated = await identity.UpdateUserAsync(userId, body.DisplayName, body.Email, role, cancellationToken);
        return Ok(ToDto(updated));
    }

    [HttpPost("{userId:guid}/deactivate")]
    public async Task<ActionResult<UserDto>> Deactivate(Guid userId, CancellationToken cancellationToken)
    {
        var users = await identity.ListUsersAsync(cancellationToken);
        var target = users.FirstOrDefault(user => user.Id == userId);
        if (target is null) return NotFound();
        if (target.Role == UserRole.Admin)
        {
            return ValidationProblem("Administrator accounts cannot be deactivated.");
        }

        var updated = await identity.SetUserStatusAsync(userId, AccountStatus.Deactivated, cancellationToken);
        return Ok(ToDto(updated));
    }

    [HttpPost("{userId:guid}/activate")]
    public async Task<ActionResult<UserDto>> Activate(Guid userId, CancellationToken cancellationToken)
    {
        var users = await identity.ListUsersAsync(cancellationToken);
        var target = users.FirstOrDefault(user => user.Id == userId);
        if (target is null) return NotFound();

        var updated = await identity.SetUserStatusAsync(userId, AccountStatus.Active, cancellationToken);
        return Ok(ToDto(updated));
    }

    [HttpPost("{userId:guid}/invite")]
    public async Task<ActionResult<UserDto>> SendInvitation(Guid userId, CancellationToken cancellationToken) =>
        Ok(ToDto(await identity.SendInvitationAsync(userId, cancellationToken)));

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Delete(Guid userId, CancellationToken cancellationToken)
    {
        var users = await identity.ListUsersAsync(cancellationToken);
        var target = users.FirstOrDefault(user => user.Id == userId);
        if (target is null) return NotFound();
        if (target.Role == UserRole.Admin)
        {
            return ValidationProblem("Administrator accounts cannot be deleted.");
        }

        var currentEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.Equals(target.Email, currentEmail, StringComparison.OrdinalIgnoreCase))
        {
            return ValidationProblem("You cannot delete your own account.");
        }

        await identity.DeleteUserAsync(userId, cancellationToken);
        return NoContent();
    }

    private static UserDto ToDto(User user) => new(
        user.Id,
        user.DisplayName,
        user.Email,
        user.Role.ToString().ToLowerInvariant(),
        user.Status.ToString().ToLowerInvariant(),
        user.MustChangePassword,
        user.InvitationSentAt,
        user.CreatedAt);
}
