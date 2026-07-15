using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

public sealed class AuthenticationTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    [Fact]
    public async Task Health_endpoint_is_public()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_seeded_admin_succeeds()
    {
        var session = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);

        Assert.False(session.MustChangePassword);
        Assert.Equal("admin", session.User.Role);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
    }

    [Fact]
    public async Task Login_with_seeded_player_succeeds()
    {
        var session = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);

        Assert.Equal("player", session.User.Role);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email = SeededAccounts.AdminEmail, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_unknown_user_returns_401()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email = "nobody@draftroom.test", password = "whatever12!A" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_without_a_token_returns_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/drafts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_only_endpoint_rejects_a_player_with_403()
    {
        var player = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        var client = factory.CreateClient().WithBearer(player.AccessToken);

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_only_endpoint_allows_an_admin()
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        var client = factory.CreateClient().WithBearer(admin.AccessToken);

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
