using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Drives the PR-12/PR-13 team-formation, ready-check, start, and spinner surface over real HTTP against the
/// in-memory host: a 2v2 draft seeds, pairs, readies, starts, and commits a server-authoritative spinner
/// order end to end; the Start control is gated on readiness; and non-host control is rejected.
/// </summary>
public sealed class DraftTeamFormationTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
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

    /// <summary>Creates a fully active account (invite → change password) and returns its id and a session.</summary>
    private async Task<(Guid UserId, HttpClient Client)> ActivePlayerAsync(string email, string name)
    {
        var admin = factory.CreateClient().WithBearer(await AdminTokenAsync());
        var create = await admin.PostAsJsonAsync("/api/users", new { email, displayName = name });
        create.EnsureSuccessStatusCode();
        var userId = (await create.Content.ReadFromJsonAsync<ManagedUser>())!.Id;

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

    private static async Task<LobbyDetail> OkAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LobbyDetail>())!;
    }

    private static async Task<LobbyDetail> CreateLobbyAsync(HttpClient host, string format, params Guid[] invites)
    {
        var response = await host.PostAsJsonAsync("/api/drafts", new { name = "Test Lobby", format, inviteUserIds = invites });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LobbyDetail>())!;
    }

    [Fact]
    public async Task TwoVsTwo_seeds_pairs_readies_starts_and_commits_a_spinner_order()
    {
        var (host, hostId) = await HostAsync();
        var (g1, c1) = await ActivePlayerAsync("tf.g1@draftroom.test", "Guest One");
        var (g2, c2) = await ActivePlayerAsync("tf.g2@draftroom.test", "Guest Two");
        var (g3, c3) = await ActivePlayerAsync("tf.g3@draftroom.test", "Guest Three");

        var lobby = await CreateLobbyAsync(host, "2v2", g1, g2, g3);
        var id = lobby.Summary.Id;
        var version = lobby.Summary.Version;

        // Everyone confirms presence.
        version = (await OkAsync(c1, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(c2, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(c3, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;

        // Lock into team formation.
        version = (await OkAsync(host, $"/api/drafts/{id}/lock", new { expectedVersion = version })).Summary.Version;

        // Host assigns two Seed 1s and two Seed 2s.
        version = (await OkAsync(host, $"/api/drafts/{id}/seeds", new { participantUserId = hostId, seed = "Seed1", expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/seeds", new { participantUserId = g1, seed = "Seed2", expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/seeds", new { participantUserId = g2, seed = "Seed1", expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/seeds", new { participantUserId = g3, seed = "Seed2", expectedVersion = version })).Summary.Version;

        // Host pairs the teams: each exactly one Seed 1 + one Seed 2.
        var teams = new[]
        {
            new TeamInput("Team Alpha", [hostId, g1]),
            new TeamInput("Team Bravo", [g2, g3]),
        };
        var formed = await OkAsync(host, $"/api/drafts/{id}/teams", new { teams, expectedVersion = version });
        version = formed.Summary.Version;
        Assert.Equal(2, formed.Teams.Count);
        Assert.True(formed.StartRequirements.AllAssigned);
        Assert.True(formed.StartRequirements.TeamsValid);
        Assert.True(formed.StartRequirements.CanBeginReadyCheck);

        // Everyone readies up.
        version = (await OkAsync(host, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(c1, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(c2, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(c3, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;

        // Host opens the ready check, then starts.
        var ready = await OkAsync(host, $"/api/drafts/{id}/ready-check", new { expectedVersion = version });
        version = ready.Summary.Version;
        Assert.Equal("ReadyCheck", ready.Summary.Status);
        Assert.True(ready.StartRequirements.CanStart);

        var started = await OkAsync(host, $"/api/drafts/{id}/start", new { expectedVersion = version });
        version = started.Summary.Version;
        Assert.Equal("SpinnerRanking", started.Summary.Status);
        Assert.NotNull(started.Summary);

        // Host commits the spinner: every team gets one unique rank.
        var spun = await OkAsync(host, $"/api/drafts/{id}/spinner", new { expectedVersion = version });
        var ranks = spun.Teams.Select(team => team.SpinnerRank).ToArray();
        Assert.All(ranks, rank => Assert.NotNull(rank));
        Assert.Equal(new[] { 1, 2 }, ranks.Select(rank => rank!.Value).OrderBy(rank => rank).ToArray());
    }

    [Fact]
    public async Task Start_is_blocked_until_everyone_is_ready()
    {
        var (host, hostId) = await HostAsync();
        var (guestId, guest) = await ActivePlayerAsync("tf.gate@draftroom.test", "Gate Guest");

        var lobby = await CreateLobbyAsync(host, "1v1", guestId);
        var id = lobby.Summary.Id;
        var version = lobby.Summary.Version;

        version = (await OkAsync(guest, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/lock", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/teams", new { teams = Array.Empty<TeamInput>(), expectedVersion = version })).Summary.Version;

        // Reach the ready check without anyone being ready.
        var ready = await OkAsync(host, $"/api/drafts/{id}/ready-check", new { expectedVersion = version });
        version = ready.Summary.Version;
        Assert.False(ready.StartRequirements.CanStart);
        Assert.False(ready.StartRequirements.AllReady);

        // Starting now is rejected server-side.
        var early = await host.PostAsJsonAsync($"/api/drafts/{id}/start", new { expectedVersion = version });
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);

        // Ready both, then start succeeds.
        version = (await OkAsync(host, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(guest, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        var started = await OkAsync(host, $"/api/drafts/{id}/start", new { expectedVersion = version });
        Assert.Equal("SpinnerRanking", started.Summary.Status);
    }

    [Fact]
    public async Task A_non_host_cannot_control_team_formation()
    {
        var (host, _) = await HostAsync();
        var (guestId, guest) = await ActivePlayerAsync("tf.nonhost@draftroom.test", "Non Host");

        var lobby = await CreateLobbyAsync(host, "2v2", guestId);
        var id = lobby.Summary.Id;
        var version = lobby.Summary.Version;
        version = (await OkAsync(guest, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;

        // Even before the lobby is lockable, a non-host attempting to seed is forbidden (host-only control).
        var seed = await guest.PostAsJsonAsync(
            $"/api/drafts/{id}/seeds", new { participantUserId = guestId, seed = "Seed1", expectedVersion = version });
        Assert.Equal(HttpStatusCode.Forbidden, seed.StatusCode);

        var teams = await guest.PostAsJsonAsync(
            $"/api/drafts/{id}/teams", new { teams = Array.Empty<TeamInput>(), expectedVersion = version });
        Assert.Equal(HttpStatusCode.Forbidden, teams.StatusCode);
    }
}
