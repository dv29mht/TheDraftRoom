using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FluentValidation;
using MediatR;

namespace FcDraft.Application.Features.Auth;

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthResponse>;

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
    IAdminNotificationService notifications)
    : IRequestHandler<LoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await identity.FindByEmailAsync(request.Email, cancellationToken);
        if (user is null || !identity.VerifyPassword(user, request.Password))
        {
            throw new UnauthorizedAppException();
        }

        if (user.Status != AccountStatus.Active)
        {
            throw new ForbiddenAppException("This account has been deactivated.");
        }

        var token = tokens.Create(user);
        if (user.Role == UserRole.Player)
        {
            notifications.Publish(
                "player.joined",
                "Player joined",
                $"{user.DisplayName} signed in to The Draft Room.");
        }
        return new AuthResponse(
            token.AccessToken,
            token.ExpiresAt,
            user.MustChangePassword,
            new AuthUserDto(user.Id, user.DisplayName, user.Email, user.Role.ToString().ToLowerInvariant()));
    }
}
