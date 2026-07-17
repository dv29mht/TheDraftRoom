using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Auth;
using FcDraft.Infrastructure.Email;
using Xunit;

namespace FcDraft.UnitTests;

public sealed class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_produces_a_bcrypt_hash_that_verifies()
    {
        var hash = _hasher.Hash("Sup3r@Secret");

        Assert.StartsWith("$2", hash);
        Assert.True(_hasher.Verify(hash, "Sup3r@Secret"));
        Assert.False(_hasher.Verify(hash, "wrong"));
    }

    [Fact]
    public void Verify_accepts_legacy_aspnet_identity_hashes()
    {
        // A hash produced by the pre-PR-05 ASP.NET Core Identity hasher must still verify so a
        // database seeded before the BCrypt switch keeps working.
        var legacy = new Microsoft.AspNetCore.Identity.PasswordHasher<object>()
            .HashPassword(new object(), "Legacy@2026Pass");

        Assert.True(_hasher.Verify(legacy, "Legacy@2026Pass"));
        Assert.False(_hasher.Verify(legacy, "nope"));
    }

    [Fact]
    public void Verify_returns_false_for_blank_or_garbage_hashes()
    {
        Assert.False(_hasher.Verify("", "anything"));
        Assert.False(_hasher.Verify("not-a-real-hash", "anything"));
    }
}

public sealed class LoginThrottleTests
{
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 07, 15, 10, 0, 0, TimeSpan.Zero));
    private readonly LoginThrottle _throttle;

    public LoginThrottleTests() => _throttle = new LoginThrottle(_clock);

    [Fact]
    public void Locks_out_after_the_configured_number_of_failures()
    {
        LockoutState state = LockoutState.Unlocked;
        for (var attempt = 0; attempt < LoginThrottle.MaxAttempts; attempt++)
        {
            Assert.False(_throttle.Check("lock@draftroom.test").IsLockedOut);
            state = _throttle.RegisterFailure("lock@draftroom.test");
        }

        Assert.True(state.IsLockedOut);
        Assert.True(_throttle.Check("lock@draftroom.test").IsLockedOut);
        Assert.True(state.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void Lockout_expires_after_the_window_elapses()
    {
        for (var attempt = 0; attempt < LoginThrottle.MaxAttempts; attempt++)
        {
            _throttle.RegisterFailure("expire@draftroom.test");
        }

        Assert.True(_throttle.Check("expire@draftroom.test").IsLockedOut);

        _clock.Advance(LoginThrottle.Lockout + TimeSpan.FromSeconds(1));
        Assert.False(_throttle.Check("expire@draftroom.test").IsLockedOut);
    }

    [Fact]
    public void A_successful_reset_clears_the_failure_count()
    {
        for (var attempt = 0; attempt < LoginThrottle.MaxAttempts - 1; attempt++)
        {
            _throttle.RegisterFailure("reset@draftroom.test");
        }

        _throttle.Reset("reset@draftroom.test");

        // A single further failure should not lock out, proving the counter reset.
        Assert.False(_throttle.RegisterFailure("reset@draftroom.test").IsLockedOut);
    }
}

public sealed class PasswordResetFlowTests
{
    private readonly RecordingInvitationEmailSender _sender = new();
    private readonly InMemoryIdentityService _service;

    public PasswordResetFlowTests() => _service = new InMemoryIdentityService(
        new DirectEmailQueue(_sender, new RecordingPasswordResetEmailSender(), new RecordingDraftEmailSender(), new RecordingAnnouncementEmailSender(), new FcDraft.Infrastructure.Email.InMemoryEmailOutbox(TimeProvider.System), Microsoft.Extensions.Logging.Abstractions.NullLogger<DirectEmailQueue>.Instance), new FakePasswordHasher());

    [Fact]
    public async Task Reset_token_sets_a_new_password_and_rotates_the_security_stamp()
    {
        var user = await _service.CreateUserAsync("Reset", "reset@draftroom.test", UserRole.Player, default);
        var stampBefore = user.SecurityStamp;

        var grant = await _service.CreatePasswordResetTokenAsync("reset@draftroom.test", default);
        Assert.NotNull(grant);

        var updated = await _service.ResetPasswordAsync(grant!.Token, "BrandNew@2026Pass", default);

        Assert.True(_service.VerifyPassword(updated, "BrandNew@2026Pass"));
        Assert.False(updated.MustChangePassword);
        Assert.NotEqual(stampBefore, updated.SecurityStamp);
    }

    [Fact]
    public async Task A_reset_token_cannot_be_used_twice()
    {
        await _service.CreateUserAsync("Once", "once@draftroom.test", UserRole.Player, default);
        var grant = (await _service.CreatePasswordResetTokenAsync("once@draftroom.test", default))!;

        await _service.ResetPasswordAsync(grant.Token, "FirstUse@2026Pass", default);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            _service.ResetPasswordAsync(grant.Token, "SecondUse@2026Pass", default));
    }

    [Fact]
    public async Task Forgot_password_returns_null_for_an_unknown_or_deactivated_account()
    {
        Assert.Null(await _service.CreatePasswordResetTokenAsync("ghost@draftroom.test", default));

        var user = await _service.CreateUserAsync("Off", "off@draftroom.test", UserRole.Player, default);
        await _service.SetUserStatusAsync(user.Id, AccountStatus.Deactivated, default);
        Assert.Null(await _service.CreatePasswordResetTokenAsync("off@draftroom.test", default));
    }

    [Fact]
    public async Task Revoking_sessions_rotates_the_security_stamp()
    {
        var user = await _service.CreateUserAsync("Revoke", "revoke@draftroom.test", UserRole.Player, default);
        var before = user.SecurityStamp;

        var after = await _service.RevokeSessionsAsync(user.Id, default);

        Assert.NotEqual(before, after.SecurityStamp);
    }
}
