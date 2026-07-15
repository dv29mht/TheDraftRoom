using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FcDraft.Application.Features.Auth;

// ── Forgot password ──────────────────────────────────────────────────────────

/// <summary>Requests a reset link. Always succeeds so the response never reveals which emails exist.</summary>
public sealed record ForgotPasswordCommand(string Email, string? IpAddress = null) : IRequest<Unit>;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() => RuleFor(request => request.Email).NotEmpty().EmailAddress();
}

public sealed class ForgotPasswordCommandHandler(
    IIdentityService identity,
    IEmailQueue emailQueue,
    ISecurityAuditService audit,
    ILogger<ForgotPasswordCommandHandler> logger)
    : IRequestHandler<ForgotPasswordCommand, Unit>
{
    public async Task<Unit> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var grant = await identity.CreatePasswordResetTokenAsync(request.Email, cancellationToken);
        if (grant is null)
        {
            // No active account: respond identically to avoid account enumeration.
            return Unit.Value;
        }

        await audit.RecordAsync(
            new SecurityAuditEntry(
                SecurityAuditAction.PasswordResetRequested,
                UserId: grant.User.Id,
                Email: grant.User.Email,
                IpAddress: request.IpAddress),
            cancellationToken);

        try
        {
            await emailQueue.EnqueuePasswordResetAsync(grant.User.Email, grant.User.DisplayName, grant.Token, cancellationToken);
        }
        catch (Exception exception)
        {
            // Best effort: the reset grant is stored, so the user can request again. Never surface a
            // delivery/queueing error to the caller, which would leak that the account exists.
            logger.LogWarning(exception, "Password reset email could not be queued for a requested reset.");
        }

        return Unit.Value;
    }
}

// ── Reset password ───────────────────────────────────────────────────────────

/// <summary>Completes a reset using the emailed token, signing the account in with a fresh token.</summary>
public sealed record ResetPasswordCommand(string Token, string NewPassword, string ConfirmPassword)
    : IRequest<AuthResponse>;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(request => request.Token).NotEmpty();
        RuleFor(request => request.NewPassword)
            .NotEmpty()
            .MinimumLength(10)
            .Matches("[A-Z]").WithMessage("Password must include an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must include a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must include a number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must include a symbol.");
        RuleFor(request => request.ConfirmPassword)
            .Equal(request => request.NewPassword).WithMessage("Passwords do not match.");
    }
}

public sealed class ResetPasswordCommandHandler(
    IIdentityService identity,
    ITokenService tokens,
    ISecurityAuditService audit)
    : IRequestHandler<ResetPasswordCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await identity.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
        await audit.RecordAsync(
            new SecurityAuditEntry(SecurityAuditAction.PasswordReset, UserId: user.Id, Email: user.Email),
            cancellationToken);

        // The reset rotated the security stamp, revoking any older session for this account.
        var token = tokens.Create(user);
        return new AuthResponse(
            token.AccessToken,
            token.ExpiresAt,
            user.MustChangePassword,
            new AuthUserDto(user.Id, user.DisplayName, user.Email, user.Role.ToString().ToLowerInvariant()));
    }
}

// ── Revoke sessions (sign out everywhere) ────────────────────────────────────

/// <summary>Rotates the account's security stamp so every existing token stops validating.</summary>
public sealed record RevokeSessionsCommand(Guid UserId) : IRequest<Unit>;

public sealed class RevokeSessionsCommandHandler(IIdentityService identity, ISecurityAuditService audit)
    : IRequestHandler<RevokeSessionsCommand, Unit>
{
    public async Task<Unit> Handle(RevokeSessionsCommand request, CancellationToken cancellationToken)
    {
        var user = await identity.RevokeSessionsAsync(request.UserId, cancellationToken);
        await audit.RecordAsync(
            new SecurityAuditEntry(SecurityAuditAction.SessionsRevoked, UserId: user.Id, Email: user.Email),
            cancellationToken);
        return Unit.Value;
    }
}
