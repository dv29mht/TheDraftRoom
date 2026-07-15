using FcDraft.Application.Common.Exceptions;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Auth;
using Xunit;

namespace FcDraft.UnitTests;

public sealed class IdentityServiceTests
{
    private readonly RecordingInvitationEmailSender _sender = new();
    private readonly InMemoryIdentityService _service;

    public IdentityServiceTests() => _service = new InMemoryIdentityService(_sender);

    [Fact]
    public async Task CreateUserAsync_invites_with_a_verifiable_one_time_password_and_forces_a_change()
    {
        var user = await _service.CreateUserAsync("New Player", "new.player@draftroom.test", UserRole.Player, default);

        Assert.True(user.MustChangePassword);
        Assert.Equal(UserRole.Player, user.Role);
        Assert.NotNull(user.InvitationSentAt);
        Assert.Equal(1, _sender.SendCount);

        var otp = _sender.PasswordFor("new.player@draftroom.test");
        Assert.True(_service.VerifyPassword(user, otp));
        Assert.False(_service.VerifyPassword(user, "not-the-otp"));
    }

    [Fact]
    public async Task CreateUserAsync_rejects_a_duplicate_email()
    {
        await _service.CreateUserAsync("First", "dupe@draftroom.test", UserRole.Player, default);

        await Assert.ThrowsAsync<ConflictAppException>(() =>
            _service.CreateUserAsync("Second", "DUPE@draftroom.test", UserRole.Player, default));
    }

    [Fact]
    public async Task CreateUserAsync_rolls_back_when_the_invitation_email_fails()
    {
        _sender.ShouldThrow = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateUserAsync("Ghost", "ghost@draftroom.test", UserRole.Player, default));

        Assert.Null(await _service.FindByEmailAsync("ghost@draftroom.test", default));
    }

    [Fact]
    public async Task ChangePasswordAsync_clears_the_force_flag_and_stores_the_new_password()
    {
        var user = await _service.CreateUserAsync("Changer", "changer@draftroom.test", UserRole.Player, default);
        var otp = _sender.PasswordFor("changer@draftroom.test");

        await _service.ChangePasswordAsync(user, otp, "Fresh@2026Pass", default);

        Assert.False(user.MustChangePassword);
        Assert.NotNull(user.PasswordChangedAt);
        Assert.True(_service.VerifyPassword(user, "Fresh@2026Pass"));
        Assert.False(_service.VerifyPassword(user, otp));
    }

    [Fact]
    public async Task ChangePasswordAsync_rejects_an_incorrect_current_password()
    {
        var user = await _service.CreateUserAsync("Wrong", "wrong@draftroom.test", UserRole.Player, default);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            _service.ChangePasswordAsync(user, "not-the-current", "Fresh@2026Pass", default));
    }

    [Fact]
    public async Task SetUserStatusAsync_toggles_the_account_status()
    {
        var user = await _service.CreateUserAsync("Toggle", "toggle@draftroom.test", UserRole.Player, default);

        var deactivated = await _service.SetUserStatusAsync(user.Id, AccountStatus.Deactivated, default);
        Assert.Equal(AccountStatus.Deactivated, deactivated.Status);

        var reactivated = await _service.SetUserStatusAsync(user.Id, AccountStatus.Active, default);
        Assert.Equal(AccountStatus.Active, reactivated.Status);
    }

    [Fact]
    public async Task SetUserStatusAsync_throws_for_an_unknown_user()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.SetUserStatusAsync(Guid.NewGuid(), AccountStatus.Deactivated, default));
    }

    [Fact]
    public async Task DeleteUserAsync_removes_the_user_from_the_directory()
    {
        var user = await _service.CreateUserAsync("Temp", "temp@draftroom.test", UserRole.Player, default);

        await _service.DeleteUserAsync(user.Id, default);

        Assert.Null(await _service.FindByEmailAsync("temp@draftroom.test", default));
    }

    [Fact]
    public async Task Constructor_seeds_the_deterministic_development_accounts()
    {
        var users = await _service.ListUsersAsync(default);

        Assert.Contains(users, u => u.Email == "admin@draftroom.dev" && u.Role == UserRole.Admin);
        Assert.Contains(users, u => u.Email == "player@draftroom.dev" && u.Role == UserRole.Player);
    }
}
