using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

public sealed class ForcedPasswordChangeTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private const string InviteeEmail = "forced.change@draftroom.test";
    private const string NewPassword = "Strong@2026Pass";

    private async Task<string> AdminTokenAsync()
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return admin.AccessToken;
    }

    [Fact]
    public async Task Invited_user_must_change_password_then_signs_in_with_the_new_one()
    {
        var admin = factory.CreateClient().WithBearer(await AdminTokenAsync());

        // Admin invites a new account.
        var create = await admin.PostAsJsonAsync("/api/users", new { email = InviteeEmail, displayName = "Invited Player" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = (await create.Content.ReadFromJsonAsync<ManagedUser>())!;
        Assert.True(created.MustChangePassword);
        Assert.Equal("active", created.Status);

        // The fake sender captured the one-time password that Brevo would have emailed.
        var oneTimePassword = factory.EmailSender.PasswordFor(InviteeEmail);

        // First sign-in reports a pending change.
        var firstLogin = await factory.CreateClient().LoginAsync(InviteeEmail, oneTimePassword);
        Assert.True(firstLogin.MustChangePassword);

        // Changing the password clears the flag and returns a fresh token.
        var changeClient = factory.CreateClient().WithBearer(firstLogin.AccessToken);
        var change = await changeClient.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = oneTimePassword,
            newPassword = NewPassword,
            confirmPassword = NewPassword
        });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var changed = (await change.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(changed.MustChangePassword);

        // The new password works; the one-time password no longer does.
        var secondLogin = await factory.CreateClient().LoginAsync(InviteeEmail, NewPassword);
        Assert.False(secondLogin.MustChangePassword);

        var staleLogin = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email = InviteeEmail, password = oneTimePassword });
        Assert.Equal(HttpStatusCode.Unauthorized, staleLogin.StatusCode);
    }

    [Fact]
    public async Task Change_password_rejects_a_weak_new_password_with_400()
    {
        const string email = "weakchange@draftroom.test";
        var admin = factory.CreateClient().WithBearer(await AdminTokenAsync());
        await admin.PostAsJsonAsync("/api/users", new { email, displayName = "Invited Player" });
        var otp = factory.EmailSender.PasswordFor(email);
        var login = await factory.CreateClient().LoginAsync(email, otp);

        var change = await factory.CreateClient().WithBearer(login.AccessToken)
            .PostAsJsonAsync("/api/auth/change-password", new
            {
                currentPassword = otp,
                newPassword = "weak",
                confirmPassword = "weak"
            });

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
    }
}
