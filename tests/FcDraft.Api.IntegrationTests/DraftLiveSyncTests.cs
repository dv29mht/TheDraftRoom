using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Proves the PR-17 done-whens over the real in-process host: two authenticated hub clients in one draft
/// group both receive the accepted-state broadcast within the PRD §14 live-propagation target (500 ms);
/// joining a draft group is authorized like <c>GET /api/drafts/{id}</c> (outsiders rejected without
/// leaking existence); a disconnected client that reconnects gets the authoritative snapshot at the latest
/// version with no duplicated actions; and a stale command surfaces as a 409 the client resolves by
/// refetching. Connections run over the TestServer transport (long polling) with the JWT carried on the
/// <c>access_token</c> query string — the same path a browser websocket uses.
/// </summary>
public sealed class DraftLiveSyncTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private const string StrongPassword = "Strong@2026Pass";
    private static readonly TimeSpan PropagationTarget = TimeSpan.FromMilliseconds(500);

    private async Task<string> AdminTokenAsync()
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return admin.AccessToken;
    }

    private async Task<(HttpClient Client, Guid UserId, string Token)> HostAsync()
    {
        var login = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        return (factory.CreateClient().WithBearer(login.AccessToken), login.User.Id, login.AccessToken);
    }

    private async Task<(Guid UserId, HttpClient Client, string Token)> ActivePlayerAsync(string email, string name)
    {
        var admin = factory.CreateClient().WithBearer(await AdminTokenAsync());
        var create = await admin.PostAsJsonAsync("/api/users", new { email, displayName = name });
        create.EnsureSuccessStatusCode();
        var userId = (await create.Content.ReadFromJsonAsync<ManagedUser>())!.Id;

        var otp = factory.EmailSender.PasswordFor(email);
        var login = await factory.CreateClient().LoginAsync(email, otp);
        var change = await factory.CreateClient().WithBearer(login.AccessToken)
            .PostAsJsonAsync("/api/auth/change-password", new { currentPassword = otp, newPassword = StrongPassword, confirmPassword = StrongPassword });
        change.EnsureSuccessStatusCode();
        var changed = (await change.Content.ReadFromJsonAsync<LoginResponse>())!;
        return (userId, factory.CreateClient().WithBearer(changed.AccessToken), changed.AccessToken);
    }

    /// <summary>A hub connection through the in-process TestServer, authenticated via the access_token query string.</summary>
    private HubConnection Connect(string token) =>
        new HubConnectionBuilder()
            .WithUrl($"http://localhost/hubs/draft?access_token={Uri.EscapeDataString(token)}", options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

    private static async Task<LobbyDetail> OkAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LobbyDetail>())!;
    }

    private async Task<(Guid DraftId, int Version, HttpClient Host, string HostToken, HttpClient Guest, string GuestToken, Guid GuestId)> LobbyAsync(string tag)
    {
        var (host, _, hostToken) = await HostAsync();
        var (guestId, guest, guestToken) = await ActivePlayerAsync($"live.{tag}@draftroom.test", $"Live {tag}");

        var create = await host.PostAsJsonAsync("/api/drafts", new { name = $"Live Lobby {tag}", format = "1v1", inviteUserIds = new[] { guestId } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var lobby = (await create.Content.ReadFromJsonAsync<LobbyDetail>())!;
        return (lobby.Summary.Id, lobby.Summary.Version, host, hostToken, guest, guestToken, guestId);
    }

    [Fact]
    public async Task Both_clients_receive_an_accepted_mutation_within_the_propagation_target()
    {
        var (draftId, version, host, hostToken, guest, guestToken, _) = await LobbyAsync("fanout");

        await using var hostHub = Connect(hostToken);
        await using var guestHub = Connect(guestToken);

        var hostReceived = new TaskCompletionSource<DraftUpdateEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var guestReceived = new TaskCompletionSource<DraftUpdateEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        hostHub.On<DraftUpdateEnvelope>("DraftUpdated", update => hostReceived.TrySetResult(update));
        guestHub.On<DraftUpdateEnvelope>("DraftUpdated", update => guestReceived.TrySetResult(update));

        await hostHub.StartAsync();
        await guestHub.StartAsync();
        var hostSnapshot = await hostHub.InvokeAsync<LobbyDetail>("JoinDraft", draftId);
        var guestSnapshot = await guestHub.InvokeAsync<LobbyDetail>("JoinDraft", draftId);
        Assert.Equal(version, hostSnapshot.Summary.Version);
        Assert.Equal(version, guestSnapshot.Summary.Version);

        // The guest confirms presence over REST; the accepted mutation must reach BOTH connected clients
        // within the PRD §14 live-propagation target.
        var accepted = await OkAsync(guest, $"/api/drafts/{draftId}/join", new { expectedVersion = version });

        var deadline = Task.Delay(PropagationTarget);
        var all = Task.WhenAll(hostReceived.Task, guestReceived.Task);
        Assert.Same(all, await Task.WhenAny(all, deadline)); // received in time, or the test fails here

        var hostUpdate = await hostReceived.Task;
        var guestUpdate = await guestReceived.Task;
        foreach (var update in new[] { hostUpdate, guestUpdate })
        {
            Assert.Equal(draftId, update.DraftId);
            Assert.Equal(accepted.Summary.Version, update.Version);
            Assert.Equal("ParticipantJoined", update.EventType);
            Assert.NotNull(update.Detail); // the envelope carries the authoritative snapshot
            Assert.Equal(accepted.Summary.Version, update.Detail!.Summary.Version);
            Assert.Contains(update.Detail.Participants, participant => participant.Status == "Joined");
        }
    }

    [Fact]
    public async Task An_outsider_cannot_join_a_draft_group()
    {
        var (draftId, _, _, _, _, _, _) = await LobbyAsync("outsider");
        var (_, _, outsiderToken) = await ActivePlayerAsync("live.outsider.x@draftroom.test", "Live Outsider");

        await using var outsiderHub = Connect(outsiderToken);
        await outsiderHub.StartAsync();

        // The rejection is 404-equivalent: it does not reveal whether the draft exists.
        var exception = await Assert.ThrowsAsync<HubException>(() => outsiderHub.InvokeAsync<LobbyDetail>("JoinDraft", draftId));
        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public async Task A_reconnecting_client_gets_the_authoritative_snapshot_without_duplicating_actions()
    {
        var (draftId, version, host, hostToken, guest, _, _) = await LobbyAsync("reconnect");

        // First connection: join the group, then "lose" the connection entirely.
        var firstHub = Connect(hostToken);
        await firstHub.StartAsync();
        var before = await firstHub.InvokeAsync<LobbyDetail>("JoinDraft", draftId);
        await firstHub.DisposeAsync();

        // While disconnected, the draft moves on.
        version = (await OkAsync(guest, $"/api/drafts/{draftId}/join", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{draftId}/lock", new { expectedVersion = version })).Summary.Version;

        // Reconnect: rejoining the group returns the authoritative snapshot at the LATEST version — the
        // client reconciles by replacing its state, never by replaying actions.
        await using var secondHub = Connect(hostToken);
        await secondHub.StartAsync();
        var after = await secondHub.InvokeAsync<LobbyDetail>("JoinDraft", draftId);

        Assert.True(after.Summary.Version > before.Summary.Version);
        Assert.Equal(version, after.Summary.Version);
        Assert.Equal("TeamFormation", after.Summary.Status);
        // No duplicated actions: exactly the events the two REST mutations appended, no more.
        Assert.Equal(before.Events.Count + 2, after.Events.Count);
        Assert.Equal(after.Events.Count, after.Events.Select(evt => evt.Sequence).Distinct().Count());

        // The REST snapshot agrees — the hub never becomes a second source of truth.
        var rest = (await host.GetFromJsonAsync<LobbyDetail>($"/api/drafts/{draftId}"))!;
        Assert.Equal(after.Summary.Version, rest.Summary.Version);
    }

    [Fact]
    public async Task A_stale_command_conflicts_and_the_client_refreshes_cleanly()
    {
        var (draftId, version, host, _, guest, _, _) = await LobbyAsync("conflict");

        // Two clients race the same version: the second submission is stale.
        await OkAsync(guest, $"/api/drafts/{draftId}/join", new { expectedVersion = version });
        var stale = await host.PostAsJsonAsync($"/api/drafts/{draftId}/lock", new { expectedVersion = version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // The refresh returns the authoritative state; retrying with the fresh version succeeds.
        var refreshed = (await host.GetFromJsonAsync<LobbyDetail>($"/api/drafts/{draftId}"))!;
        var locked = await OkAsync(host, $"/api/drafts/{draftId}/lock", new { expectedVersion = refreshed.Summary.Version });
        Assert.Equal("TeamFormation", locked.Summary.Status);
    }
}
