using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace FcDraft.API.Controllers;

/// <summary>
/// Records one §9.10 admin action (PR-21) with uniform attribution: the acting admin's id and email
/// from the request's claims, the caller's IP, and a human-readable detail naming the target. Shared
/// by every admin controller so the audit trail reads consistently.
/// </summary>
public static class AdminActionAudit
{
    public static Task RecordAdminActionAsync(
        this ISecurityAuditService audit,
        ControllerBase controller,
        SecurityAuditAction action,
        string detail,
        CancellationToken cancellationToken)
    {
        var user = controller.User;
        Guid.TryParse(
            user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var callerId);
        return audit.RecordAsync(new SecurityAuditEntry(
            action,
            UserId: callerId == Guid.Empty ? null : callerId,
            Email: user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue(JwtRegisteredClaimNames.Email),
            Detail: detail,
            IpAddress: controller.HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
    }
}
