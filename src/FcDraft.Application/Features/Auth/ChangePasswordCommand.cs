using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Auth;

public sealed record ChangePasswordCommand(
    string Email,
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword) : IRequest<AuthResponse>;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress();
        RuleFor(request => request.CurrentPassword).NotEmpty();
        RuleFor(request => request.NewPassword)
            .NotEmpty()
            .MinimumLength(10)
            .Matches("[A-Z]").WithMessage("Password must include an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must include a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must include a number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must include a symbol.")
            .NotEqual(request => request.CurrentPassword).WithMessage("Choose a password different from the temporary password.");
        RuleFor(request => request.ConfirmPassword)
            .Equal(request => request.NewPassword).WithMessage("Passwords do not match.");
    }
}

public sealed class ChangePasswordCommandHandler(
    IIdentityService identity,
    ITokenService tokens,
    ISecurityAuditService audit,
    IProductAnalytics? analytics = null)
    : IRequestHandler<ChangePasswordCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await identity.FindByEmailAsync(request.Email, cancellationToken)
            ?? throw new Common.Exceptions.UnauthorizedAppException();

        // Captured before the change clears the flag: completing the FORCED first change is the §15
        // invite-to-activation conversion event; a routine profile password change is not.
        var wasForcedFirstChange = user.MustChangePassword;

        await identity.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword, cancellationToken);
        await audit.RecordAsync(
            new SecurityAuditEntry(SecurityAuditAction.PasswordChanged, UserId: user.Id, Email: user.Email),
            cancellationToken);

        if (wasForcedFirstChange)
        {
            (analytics ?? NullProductAnalytics.Instance).UserActivated();
        }

        // ChangePasswordAsync rotated the security stamp, so the freshly minted token is the only one
        // that will still validate — every earlier session for this account is now revoked.
        var token = tokens.Create(user);
        return new AuthResponse(
            token.AccessToken,
            token.ExpiresAt,
            user.MustChangePassword,
            new AuthUserDto(user.Id, user.DisplayName, user.Email, user.Role.ToString().ToLowerInvariant()));
    }
}
