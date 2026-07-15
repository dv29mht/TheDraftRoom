using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

public sealed class AccountLifecycleTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private async Task<(HttpClient admin, LoginResponse session)> AdminAsync()
    {
        var session = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return (factory.CreateClient().WithBearer(session.AccessToken), session);
    }

    /// <summary>Invites a user, activates it with a known password, and returns its id + password.</summary>
    private async Task<(Guid id, string password)> CreateSignedInUserAsync(HttpClient admin, string email)
    {
        var create = await admin.PostAsJsonAsync("/api/users", new { email, displayName = "Lifecycle Tester" });
        var created = (await create.Content.ReadFromJsonAsync<ManagedUser>())!;
        var otp = factory.EmailSender.PasswordFor(email);
        var login = await factory.CreateClient().LoginAsync(email, otp);
        const string password = "Lifecycle@2026Ok";
        await factory.CreateClient().WithBearer(login.AccessToken).PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = otp,
            newPassword = password,
            confirmPassword = password
        });
        return (created.Id, password);
    }

    [Fact]
    public async Task Deactivation_blocks_login_and_a_pre_existing_token_from_creating_rooms()
    {
        var (admin, _) = await AdminAsync();
        var (userId, password) = await CreateSignedInUserAsync(admin, "deactivate.me@draftroom.test");

        // Token obtained while still active.
        var activeSession = await factory.CreateClient().LoginAsync("deactivate.me@draftroom.test", password);

        // Admin deactivates the account.
        var deactivate = await admin.PostAsync($"/api/users/{userId}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var deactivated = (await deactivate.Content.ReadFromJsonAsync<ManagedUser>())!;
        Assert.Equal("deactivated", deactivated.Status);

        // Fresh login is now forbidden.
        var blockedLogin = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email = "deactivate.me@draftroom.test", password });
        Assert.Equal(HttpStatusCode.Forbidden, blockedLogin.StatusCode);

        // The token issued before deactivation is now revoked at the auth layer (PR-05 security
        // stamp / status re-check on every request), so it is rejected as unauthorized before ever
        // reaching the drafts controller.
        var roomAttempt = await factory.CreateClient().WithBearer(activeSession.AccessToken)
            .PostAsJsonAsync("/api/drafts", new { name = "Blocked Lobby", format = "1v1" });
        Assert.Equal(HttpStatusCode.Unauthorized, roomAttempt.StatusCode);

        // Reactivation restores access.
        var activate = await admin.PostAsync($"/api/users/{userId}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var restored = await factory.CreateClient().LoginAsync("deactivate.me@draftroom.test", password);
        Assert.Equal("player", restored.User.Role);
    }

    [Fact]
    public async Task Creating_a_user_requires_a_name_and_email()
    {
        var (admin, _) = await AdminAsync();

        var missingName = await admin.PostAsJsonAsync("/api/users", new { email = "no.name@draftroom.test" });
        Assert.Equal(HttpStatusCode.BadRequest, missingName.StatusCode);

        var blankName = await admin.PostAsJsonAsync("/api/users", new { email = "blank.name@draftroom.test", displayName = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);

        var missingEmail = await admin.PostAsJsonAsync("/api/users", new { displayName = "No Email" });
        Assert.Equal(HttpStatusCode.BadRequest, missingEmail.StatusCode);

        var valid = await admin.PostAsJsonAsync("/api/users", new { email = "named.player@draftroom.test", displayName = "Named Player" });
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        var created = (await valid.Content.ReadFromJsonAsync<ManagedUser>())!;
        Assert.Equal("Named Player", created.DisplayName);
    }

    [Fact]
    public async Task Administrator_accounts_are_protected_from_deactivation()
    {
        var (admin, session) = await AdminAsync();

        var response = await admin.PostAsync($"/api/users/{session.User.Id}/deactivate", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deactivating_an_unknown_user_returns_404()
    {
        var (admin, _) = await AdminAsync();

        var response = await admin.PostAsync($"/api/users/{Guid.NewGuid()}/deactivate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
