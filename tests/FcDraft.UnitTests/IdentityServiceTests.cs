using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Auth;
using FcDraft.Infrastructure.Email;
using Xunit;

namespace FcDraft.UnitTests;

public sealed class IdentityServiceTests
{
    private readonly RecordingInvitationEmailSender _sender = new();
    private readonly InMemoryIdentityService _service;

    public IdentityServiceTests() => _service = new InMemoryIdentityService(
        new DirectEmailQueue(_sender, new RecordingPasswordResetEmailSender()), new FakePasswordHasher());

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
    public async Task UpdateUserAsync_persists_optional_profile_fields_and_trims_blanks_to_null()
    {
        var user = await _service.CreateUserAsync("Profile", "profile@draftroom.test", UserRole.Player, default);

        var withProfile = await _service.UpdateUserAsync(
            user.Id,
            new UserProfileUpdate("Profile", "profile@draftroom.test", UserRole.Player, "https://cdn/a.png", "  Galácticos  "),
            default);
        Assert.Equal("https://cdn/a.png", withProfile.AvatarUrl);
        Assert.Equal("Galácticos", withProfile.PreferredTeamName);

        var cleared = await _service.UpdateUserAsync(
            user.Id,
            new UserProfileUpdate("Profile", "profile@draftroom.test", UserRole.Player, "", "   "),
            default);
        Assert.Null(cleared.AvatarUrl);
        Assert.Null(cleared.PreferredTeamName);
    }

    [Fact]
    public async Task SearchUsersAsync_pages_filters_and_reports_directory_wide_tallies()
    {
        for (var index = 0; index < 12; index++)
        {
            await _service.CreateUserAsync($"Bulk Player {index:00}", $"bulk{index:00}@draftroom.test", UserRole.Player, default);
        }

        // Page 1 of 10 across the whole directory (12 bulk + 2 seeded = 14).
        var firstPage = await _service.SearchUsersAsync(new UserDirectoryQuery(null, 1, 10), default);
        Assert.Equal(14, firstPage.Total);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(10, firstPage.Items.Count);
        Assert.Equal(12, firstPage.InvitedCount); // the 12 invited bulk accounts; seeded accounts were not invited

        // An out-of-range page clamps to the last page.
        var lastPage = await _service.SearchUsersAsync(new UserDirectoryQuery(null, 99, 10), default);
        Assert.Equal(2, lastPage.Page);
        Assert.Equal(4, lastPage.Items.Count);

        // Search narrows the total and is case-insensitive.
        var filtered = await _service.SearchUsersAsync(new UserDirectoryQuery("BULK PLAYER 05", 1, 10), default);
        Assert.Equal(1, filtered.Total);
        Assert.Equal("bulk05@draftroom.test", filtered.Items.Single().Email);
    }

    [Fact]
    public async Task Constructor_seeds_the_deterministic_development_accounts()
    {
        var directory = await _service.SearchUsersAsync(new UserDirectoryQuery(null, 1, 50), default);

        Assert.Contains(directory.Items, u => u.Email == "mdevansh@gmail.com" && u.Role == UserRole.Admin);
        Assert.Contains(directory.Items, u => u.Email == "player@draftroom.dev" && u.Role == UserRole.Player);
    }
}
