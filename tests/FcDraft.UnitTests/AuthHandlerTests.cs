using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Features.Auth;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Auth;
using Xunit;

namespace FcDraft.UnitTests;

public sealed class AuthHandlerTests
{
    private readonly RecordingInvitationEmailSender _sender = new();
    private readonly InMemoryIdentityService _identity;
    private readonly FakeTokenService _tokens = new();
    private readonly RecordingAdminNotificationService _notifications = new();

    public AuthHandlerTests() => _identity = new InMemoryIdentityService(_sender);

    private LoginCommandHandler Login() => new(_identity, _tokens, _notifications);

    /// <summary>Creates an activated account with a known password, mirroring the invite -> first-change flow.</summary>
    private async Task<User> CreateActiveUserAsync(string email, string password, UserRole role)
    {
        var user = await _identity.CreateUserAsync(email.Split('@')[0], email, role, default);
        await _identity.ChangePasswordAsync(user, _sender.PasswordFor(email), password, default);
        return user;
    }

    [Fact]
    public async Task Login_returns_a_token_for_valid_credentials()
    {
        await CreateActiveUserAsync("valid@draftroom.test", "Valid@2026Pass", UserRole.Player);

        var response = await Login().Handle(new LoginCommand("valid@draftroom.test", "Valid@2026Pass"), default);

        Assert.StartsWith("test-token::", response.AccessToken);
        Assert.False(response.MustChangePassword);
        Assert.Equal("valid@draftroom.test", response.User.Email);
        Assert.Equal("player", response.User.Role);
    }

    [Fact]
    public async Task Login_reports_a_pending_password_change_after_invitation()
    {
        var user = await _identity.CreateUserAsync("Pending", "pending@draftroom.test", UserRole.Player, default);
        var otp = _sender.PasswordFor("pending@draftroom.test");

        var response = await Login().Handle(new LoginCommand(user.Email, otp), default);

        Assert.True(response.MustChangePassword);
    }

    [Fact]
    public async Task Login_rejects_an_unknown_user()
    {
        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            Login().Handle(new LoginCommand("nobody@draftroom.test", "whatever"), default));
    }

    [Fact]
    public async Task Login_rejects_a_wrong_password()
    {
        await CreateActiveUserAsync("pw@draftroom.test", "Correct@2026Pass", UserRole.Player);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            Login().Handle(new LoginCommand("pw@draftroom.test", "Wrong@2026Pass"), default));
    }

    [Fact]
    public async Task Login_forbids_a_deactivated_account()
    {
        var user = await CreateActiveUserAsync("gone@draftroom.test", "Gone@2026Pass", UserRole.Player);
        await _identity.SetUserStatusAsync(user.Id, AccountStatus.Deactivated, default);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Login().Handle(new LoginCommand("gone@draftroom.test", "Gone@2026Pass"), default));
    }

    [Fact]
    public async Task Login_announces_a_player_sign_in_but_not_an_admin_sign_in()
    {
        await CreateActiveUserAsync("p1@draftroom.test", "Player@2026Pass", UserRole.Player);
        await CreateActiveUserAsync("a1@draftroom.test", "Admin@2026Pass", UserRole.Admin);

        await Login().Handle(new LoginCommand("p1@draftroom.test", "Player@2026Pass"), default);
        await Login().Handle(new LoginCommand("a1@draftroom.test", "Admin@2026Pass"), default);

        Assert.Single(_notifications.Published);
        Assert.Equal("player.joined", _notifications.Published[0].Type);
    }

    [Fact]
    public async Task ChangePassword_issues_a_fresh_token_and_clears_the_force_flag()
    {
        var user = await _identity.CreateUserAsync("Cp", "cp@draftroom.test", UserRole.Player, default);
        var otp = _sender.PasswordFor("cp@draftroom.test");
        var handler = new ChangePasswordCommandHandler(_identity, _tokens);

        var response = await handler.Handle(
            new ChangePasswordCommand(user.Email, otp, "Brand@2026New1", "Brand@2026New1"), default);

        Assert.False(response.MustChangePassword);
        Assert.StartsWith("test-token::", response.AccessToken);
        Assert.True(_identity.VerifyPassword(user, "Brand@2026New1"));
    }

    [Fact]
    public async Task ChangePassword_rejects_a_wrong_current_password()
    {
        await _identity.CreateUserAsync("Cp2", "cp2@draftroom.test", UserRole.Player, default);
        var handler = new ChangePasswordCommandHandler(_identity, _tokens);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            handler.Handle(new ChangePasswordCommand("cp2@draftroom.test", "wrong", "Brand@2026New1", "Brand@2026New1"), default));
    }
}
