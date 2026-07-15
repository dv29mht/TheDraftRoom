using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Covers the PR-05 security boundary end-to-end against the real API: forced-password-change
/// enforcement, session revocation via the security stamp (sign-out-everywhere), failed-login
/// lockout, and the forgot/reset-password flow.
/// </summary>
public sealed class AuthSecurityTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private async Task<HttpClient> AdminAsync()
    {
        var session = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return factory.CreateClient().WithBearer(session.AccessToken);
    }

    /// <summary>Invites and activates an account with a known password; returns its id and password.</summary>
    private async Task<(Guid id, string password)> CreateActiveUserAsync(HttpClient admin, string email)
    {
        var create = await admin.PostAsJsonAsync("/api/users", new { email, displayName = "Security Tester" });
        var created = (await create.Content.ReadFromJsonAsync<ManagedUser>())!;
        var otp = factory.EmailSender.PasswordFor(email);
        var firstLogin = await factory.CreateClient().LoginAsync(email, otp);
        const string password = "Secure@2026Pass";
        await factory.CreateClient().WithBearer(firstLogin.AccessToken).PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = otp,
            newPassword = password,
            confirmPassword = password
        });
        return (created.Id, password);
    }

    [Fact]
    public async Task A_must_change_password_token_is_blocked_from_every_other_endpoint()
    {
        const string email = "mustchange.block@draftroom.test";
        var admin = await AdminAsync();
        await admin.PostAsJsonAsync("/api/users", new { email, displayName = "Must Change" });
        var otp = factory.EmailSender.PasswordFor(email);

        var firstLogin = await factory.CreateClient().LoginAsync(email, otp);
        Assert.True(firstLogin.MustChangePassword);
        var client = factory.CreateClient().WithBearer(firstLogin.AccessToken);

        // Any authenticated endpoint other than change-password is refused with 403.
        var blocked = await client.GetAsync("/api/drafts");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        // After changing the password, the same account can reach the endpoint.
        const string password = "Unblocked@2026Pass";
        var change = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = otp,
            newPassword = password,
            confirmPassword = password
        });
        var changed = (await change.Content.ReadFromJsonAsync<LoginResponse>())!;
        var allowed = await factory.CreateClient().WithBearer(changed.AccessToken).GetAsync("/api/drafts");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Sign_out_everywhere_revokes_previously_issued_tokens()
    {
        var admin = await AdminAsync();
        var (_, password) = await CreateActiveUserAsync(admin, "revoke.me@draftroom.test");

        var session = await factory.CreateClient().LoginAsync("revoke.me@draftroom.test", password);
        var client = factory.CreateClient().WithBearer(session.AccessToken);

        // The token works before revocation.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/drafts")).StatusCode);

        // Signing out everywhere rotates the security stamp.
        var revoke = await client.PostAsync("/api/auth/logout-all", null);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        // The previously issued token no longer validates.
        var afterRevoke = await factory.CreateClient().WithBearer(session.AccessToken).GetAsync("/api/drafts");
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);

        // A fresh sign-in still works.
        var fresh = await factory.CreateClient().LoginAsync("revoke.me@draftroom.test", password);
        Assert.False(fresh.MustChangePassword);
    }

    [Fact]
    public async Task Repeated_failed_sign_ins_are_locked_out()
    {
        const string email = "lockout@draftroom.test";
        var admin = await AdminAsync();
        await CreateActiveUserAsync(admin, email);

        // Five wrong passwords are rejected as unauthorized.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var bad = await factory.CreateClient()
                .PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong@2026Pass" });
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }

        // The account is now locked out; even the correct password is refused with 429.
        var locked = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email, password = "Secure@2026Pass" });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task Forgot_and_reset_password_signs_in_with_the_new_password()
    {
        const string email = "reset.flow@draftroom.test";
        var admin = await AdminAsync();
        var (_, oldPassword) = await CreateActiveUserAsync(admin, email);

        // Requesting a reset always succeeds (no account enumeration) and emails a token.
        var forgot = await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        Assert.True(factory.ResetEmailSender.TryGetToken(email, out var token));

        // Completing the reset returns a signed-in session.
        const string newPassword = "Reset@2026Fresh";
        var reset = await factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password", new
        {
            token,
            newPassword,
            confirmPassword = newPassword
        });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var session = (await reset.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(session.MustChangePassword);

        // The new password works; the old one does not.
        var newLogin = await factory.CreateClient().LoginAsync(email, newPassword);
        Assert.False(newLogin.MustChangePassword);
        var oldLogin = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email, password = oldPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
    }

    [Fact]
    public async Task Forgot_password_for_an_unknown_email_still_returns_accepted()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/forgot-password", new { email = "nobody@draftroom.test" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}
