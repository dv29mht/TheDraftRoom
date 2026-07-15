using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Drives the PR-11 lobby surface over real HTTP against the in-memory host: a host creates a lobby and can
/// reopen its authoritative snapshot; invites reject deactivated users; a participant joins; a host removes
/// a participant; the capacity rules (1v1 2–10, 2v2 4–16 even) gate the lock server-side; and only the
/// lobby's participants (or an admin) can open it.
/// </summary>
public sealed class DraftLobbyTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private const string StrongPassword = "Strong@2026Pass";

    private async Task<string> AdminTokenAsync()
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return admin.AccessToken;
    }

    private async Task<(HttpClient Client, Guid UserId)> HostAsync()
    {
        var login = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        return (factory.CreateClient().WithBearer(login.AccessToken), login.User.Id);
    }

    /// <summary>Invites an account (active, but still must-change) that the host can invite by id.</summary>
    private async Task<Guid> InviteableUserAsync(string email, string name)
    {
        var admin = factory.CreateClient().WithBearer(await AdminTokenAsync());
        var create = await admin.PostAsJsonAsync("/api/users", new { email, displayName = name });
        create.EnsureSuccessStatusCode();
        return (await create.Content.ReadFromJsonAsync<ManagedUser>())!.Id;
    }

    /// <summary>Creates a fully active account (invite → change password) and returns its id and a session.</summary>
    private async Task<(Guid UserId, HttpClient Client)> ActivePlayerAsync(string email, string name)
    {
        var userId = await InviteableUserAsync(email, name);
        var otp = factory.EmailSender.PasswordFor(email);
        var login = await factory.CreateClient().LoginAsync(email, otp);
        var change = await factory.CreateClient().WithBearer(login.AccessToken)
            .PostAsJsonAsync("/api/auth/change-password", new
            {
                currentPassword = otp,
                newPassword = StrongPassword,
                confirmPassword = StrongPassword
            });
        change.EnsureSuccessStatusCode();
        var changed = (await change.Content.ReadFromJsonAsync<LoginResponse>())!;
        return (userId, factory.CreateClient().WithBearer(changed.AccessToken));
    }

    private static async Task<LobbyDetail> CreateLobbyAsync(HttpClient host, string format, params Guid[] invites)
    {
        var response = await host.PostAsJsonAsync("/api/drafts", new { name = "Test Lobby", format, inviteUserIds = invites });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LobbyDetail>())!;
    }

    [Fact]
    public async Task Host_creates_a_lobby_and_can_reopen_the_authoritative_snapshot()
    {
        var (host, hostId) = await HostAsync();

        var created = await CreateLobbyAsync(host, "1v1");
        Assert.Equal("Lobby", created.Summary.Status);
        Assert.Equal(hostId, created.Summary.HostUserId);
        var hostParticipant = Assert.Single(created.Participants);
        Assert.True(hostParticipant.IsHost);
        Assert.Equal("Joined", hostParticipant.Status);
        Assert.Equal(2, created.Capacity.Min);
        Assert.Equal(10, created.Capacity.Max);

        // Any participant (here the host) can reopen the same snapshot.
        var snapshot = await host.GetFromJsonAsync<LobbyDetail>($"/api/drafts/{created.Summary.Id}");
        Assert.Equal(created.Summary.Version, snapshot!.Summary.Version);
        Assert.Equal(hostParticipant.UserId, Assert.Single(snapshot.Participants).UserId);
    }

    [Fact]
    public async Task Inviting_a_deactivated_user_is_rejected()
    {
        var (host, _) = await HostAsync();
        var lobby = await CreateLobbyAsync(host, "1v1");

        var deactivatedId = await InviteableUserAsync("lobby.deactivated@draftroom.test", "Benched");
        var admin = factory.CreateClient().WithBearer(await AdminTokenAsync());
        (await admin.PostAsync($"/api/users/{deactivatedId}/deactivate", null)).EnsureSuccessStatusCode();

        var invite = await host.PostAsJsonAsync(
            $"/api/drafts/{lobby.Summary.Id}/invite",
            new { inviteUserId = deactivatedId, expectedVersion = lobby.Summary.Version });

        Assert.Equal(HttpStatusCode.BadRequest, invite.StatusCode);
    }

    [Fact]
    public async Task An_invited_participant_can_join_and_the_host_can_remove_one()
    {
        var (host, _) = await HostAsync();
        var (guestId, guest) = await ActivePlayerAsync("lobby.guest@draftroom.test", "Guest");
        var lobby = await CreateLobbyAsync(host, "2v2", guestId);

        // The guest confirms presence.
        var join = await guest.PostAsJsonAsync(
            $"/api/drafts/{lobby.Summary.Id}/join", new { expectedVersion = lobby.Summary.Version });
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        var joined = (await join.Content.ReadFromJsonAsync<LobbyDetail>())!;
        Assert.Contains(joined.Participants, p => p.UserId == guestId && p.Status == "Joined");

        // The host removes them again before start.
        var remove = await host.PostAsJsonAsync(
            $"/api/drafts/{lobby.Summary.Id}/participants/{guestId}/remove",
            new { expectedVersion = joined.Summary.Version });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        var afterRemove = (await remove.Content.ReadFromJsonAsync<LobbyDetail>())!;
        Assert.DoesNotContain(afterRemove.Participants, p => p.UserId == guestId);
    }

    [Fact]
    public async Task A_1v1_lobby_below_the_minimum_cannot_lock_but_a_valid_one_can()
    {
        var (host, _) = await HostAsync();
        var lobby = await CreateLobbyAsync(host, "1v1");

        // Host only (1) is below the 1v1 minimum of 2.
        var tooSmall = await host.PostAsJsonAsync(
            $"/api/drafts/{lobby.Summary.Id}/lock", new { expectedVersion = lobby.Summary.Version });
        Assert.Equal(HttpStatusCode.BadRequest, tooSmall.StatusCode);

        // Invite a second active user, then the lobby locks into team formation.
        var guestId = await InviteableUserAsync("lobby.second@draftroom.test", "Second");
        var invite = await host.PostAsJsonAsync(
            $"/api/drafts/{lobby.Summary.Id}/invite",
            new { inviteUserId = guestId, expectedVersion = lobby.Summary.Version });
        invite.EnsureSuccessStatusCode();
        var withGuest = (await invite.Content.ReadFromJsonAsync<LobbyDetail>())!;

        var lockResponse = await host.PostAsJsonAsync(
            $"/api/drafts/{lobby.Summary.Id}/lock", new { expectedVersion = withGuest.Summary.Version });
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);
        var locked = (await lockResponse.Content.ReadFromJsonAsync<LobbyDetail>())!;
        Assert.Equal("TeamFormation", locked.Summary.Status);
    }

    [Fact]
    public async Task A_2v2_lobby_with_an_odd_count_cannot_lock()
    {
        var (host, _) = await HostAsync();
        var guestId = await InviteableUserAsync("lobby.odd@draftroom.test", "Odd One");
        var lobby = await CreateLobbyAsync(host, "2v2", guestId); // host + 1 = 2, below the 2v2 minimum of 4

        var lockResponse = await host.PostAsJsonAsync(
            $"/api/drafts/{lobby.Summary.Id}/lock", new { expectedVersion = lobby.Summary.Version });

        Assert.Equal(HttpStatusCode.BadRequest, lockResponse.StatusCode);
    }

    [Fact]
    public async Task A_non_participant_cannot_open_the_lobby_but_an_admin_can()
    {
        var (host, _) = await HostAsync();
        var lobby = await CreateLobbyAsync(host, "1v1");

        var (_, outsider) = await ActivePlayerAsync("lobby.outsider@draftroom.test", "Outsider");
        var outsiderGet = await outsider.GetAsync($"/api/drafts/{lobby.Summary.Id}");
        Assert.Equal(HttpStatusCode.NotFound, outsiderGet.StatusCode);

        var admin = factory.CreateClient().WithBearer(await AdminTokenAsync());
        var adminGet = await admin.GetAsync($"/api/drafts/{lobby.Summary.Id}");
        Assert.Equal(HttpStatusCode.OK, adminGet.StatusCode);
    }

    [Fact]
    public async Task Creating_a_lobby_without_a_token_returns_401()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/drafts", new { name = "Anon Lobby", format = "1v1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
