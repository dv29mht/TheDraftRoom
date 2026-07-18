using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Auth;

public sealed record LoginCommand(string Email, string Password, string? IpAddress = null) : IRequest<AuthResponse>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress();
        RuleFor(request => request.Password).NotEmpty();
    }
}

public sealed class LoginCommandHandler(
    IIdentityService identity,
    ITokenService tokens,
    ILoginThrottle throttle,
    ISecurityAuditService audit,
    IAdminNotificationService notifications)
    : IRequestHandler<LoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        // Reject while locked out, before touching the store, so lockout also throttles the work done.
        var lockout = throttle.Check(request.Email);
        if (lockout.IsLockedOut)
        {
            await audit.RecordAsync(
                new SecurityAuditEntry(
                    SecurityAuditAction.SignInLockedOut,
                    Email: request.Email,
                    Detail: $"Locked; retry in {(int)lockout.RetryAfter.TotalSeconds}s",
                    IpAddress: request.IpAddress),
                cancellationToken);
            throw new TooManyRequestsAppException(
                "Too many failed sign-in attempts. Try again later or reset your password.");
        }

        var user = await identity.FindByEmailAsync(request.Email, cancellationToken);
        if (user is null || !identity.VerifyPassword(user, request.Password))
        {
            throttle.RegisterFailure(request.Email);
            await audit.RecordAsync(
                new SecurityAuditEntry(
                    SecurityAuditAction.SignInFailed,
                    UserId: user?.Id,
                    Email: request.Email,
                    IpAddress: request.IpAddress),
                cancellationToken);
            throw new UnauthorizedAppException();
        }

        if (user.Status != AccountStatus.Active)
        {
            await audit.RecordAsync(
                new SecurityAuditEntry(
                    SecurityAuditAction.SignInFailed,
                    UserId: user.Id,
                    Email: request.Email,
                    Detail: "Account deactivated",
                    IpAddress: request.IpAddress),
                cancellationToken);
            throw new ForbiddenAppException("This account has been deactivated.");
        }

        throttle.Reset(request.Email);
        var token = tokens.Create(user);
        await audit.RecordAsync(
            new SecurityAuditEntry(
                SecurityAuditAction.SignInSucceeded,
                UserId: user.Id,
                Email: user.Email,
                IpAddress: request.IpAddress),
            cancellationToken);

        if (user.Role == UserRole.Player)
        {
            notifications.Publish(
                "player.joined",
                "Player joined",
                $"{user.DisplayName} signed in to ROSTR.");
        }

        return new AuthResponse(
            token.AccessToken,
            token.ExpiresAt,
            user.MustChangePassword,
            new AuthUserDto(user.Id, user.DisplayName, user.Email, user.Role.ToString().ToLowerInvariant()));
    }
}
