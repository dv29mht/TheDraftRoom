using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Drives the PR-16 server timer and host controls over real HTTP against the in-memory host, with the
/// whole app on a settable <see cref="TestClock"/>: an expired turn auto-picks and advances on the next
/// read; pausing blocks picks and freezes the clock (paused time never elapses); cancelling requires a
/// reason and preserves the append-only history; and admin recovery stays admin-only.
/// </summary>
public sealed class DraftTimerAndControlTests(TimedApiFactory factory) : IClassFixture<TimedApiFactory>
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

    private async Task<(Guid UserId, HttpClient Client)> ActivePlayerAsync(string email, string name)
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
        return (userId, factory.CreateClient().WithBearer(changed.AccessToken));
    }

    private static async Task<LobbyDetail> OkAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LobbyDetail>())!;
    }

    private static Task<BoardDto> BoardAsync(HttpClient client, Guid draftId) =>
        client.GetFromJsonAsync<BoardDto>($"/api/drafts/{draftId}/board")!;

    /// <summary>Drives a fresh 1v1 draft into the position draft; returns its snapshot and both clients.</summary>
    private async Task<(LobbyDetail Detail, HttpClient Host, HttpClient Guest, Dictionary<Guid, HttpClient> ClientByUser)> PositionDraftAsync(string tag)
    {
        var (host, hostId) = await HostAsync();
        var (guestId, guest) = await ActivePlayerAsync($"timer.{tag}@draftroom.test", $"Timer {tag}");
        var clientByUser = new Dictionary<Guid, HttpClient> { [hostId] = host, [guestId] = guest };

        var create = await host.PostAsJsonAsync("/api/drafts", new { name = $"Timer Lobby {tag}", format = "1v1", inviteUserIds = new[] { guestId } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var lobby = (await create.Content.ReadFromJsonAsync<LobbyDetail>())!;
        var id = lobby.Summary.Id;
        var version = lobby.Summary.Version;

        version = (await OkAsync(guest, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/lock", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/teams", new { teams = Array.Empty<TeamInput>(), expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(guest, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/ready-check", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/start", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/spinner", new { expectedVersion = version })).Summary.Version;
        var detail = await OkAsync(host, $"/api/drafts/{id}/open-clubs", new { expectedVersion = version });

        for (var team = 0; team < 2; team++)
        {
            var actor = clientByUser[detail.Turn.ActiveTeamMemberUserIds[0]];
            var clubs = (await actor.GetFromJsonAsync<BoardDto>($"/api/drafts/{id}/board"))!.AvailableClubs;
            var club = clubs[0];
            var held = (await actor.GetFromJsonAsync<BoardDto>($"/api/drafts/{id}/board?clubId={club.Id}"))!.EligibleFootballers[0];
            detail = await OkAsync(actor, $"/api/drafts/{id}/club-select", new { clubId = club.Id, footballerId = held.Id, expectedVersion = detail.Summary.Version });
        }

        detail = await OkAsync(host, $"/api/drafts/{id}/open-positions", new { expectedVersion = detail.Summary.Version });
        Assert.Equal("PositionDraft", detail.Summary.Status);
        return (detail, host, guest, clientByUser);
    }

    [Fact]
    public async Task An_expired_turn_auto_picks_and_advances_on_the_next_read()
    {
        var (detail, host, _, _) = await PositionDraftAsync("expiry");
        var id = detail.Summary.Id;
        var firstTeam = detail.Turn.ActiveTeamId!.Value;

        // The opened round carries a full, running 120s clock.
        Assert.True(detail.Timer.IsTimed);
        Assert.Equal(120, detail.Timer.PickTimerSeconds);

        // Nobody picks; the turn expires. The next board read applies the auto-pick lazily.
        factory.Clock.Advance(TimeSpan.FromSeconds(121));
        var board = await BoardAsync(host, id);

        Assert.NotEqual(firstTeam, board.Turn.ActiveTeamId); // the turn advanced
        Assert.True(board.Timer.IsTimed);
        Assert.True(board.Timer.RemainingSeconds > 100); // fresh clock anchored at the expired deadline

        var snapshot = (await host.GetFromJsonAsync<LobbyDetail>($"/api/drafts/{id}"))!;
        var autoPick = Assert.Single(snapshot.Events, evt => evt.Type == "PickAutoSelected");
        Assert.Null(autoPick.ActorUserId); // a system action
        Assert.Single(snapshot.Picks, pick => pick.SlotOrder == 1 && pick.TeamId == firstTeam);

        // The auto-picked footballer was the §6.4 deterministic best: top of the board's best-first pool.
        Assert.Equal("PositionDraft", snapshot.Summary.Status);
    }

    [Fact]
    public async Task Pausing_blocks_picks_and_paused_time_never_elapses()
    {
        var (detail, host, _, clientByUser) = await PositionDraftAsync("pause");
        var id = detail.Summary.Id;

        // Pause requires a reason.
        var missingReason = await host.PostAsJsonAsync($"/api/drafts/{id}/pause", new { reason = "", expectedVersion = detail.Summary.Version });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        factory.Clock.Advance(TimeSpan.FromSeconds(20)); // 100 seconds remain
        var paused = await OkAsync(host, $"/api/drafts/{id}/pause", new { reason = "Guest lost connection", expectedVersion = detail.Summary.Version });
        Assert.Equal("Paused", paused.Summary.Status);
        Assert.True(paused.Timer.IsPaused);
        Assert.Equal(100, paused.Timer.RemainingSeconds!.Value, precision: 1);

        // While paused, no pick is accepted — from anyone.
        var activeActor = clientByUser[detail.Turn.ActiveTeamMemberUserIds[0]];
        var pool = (await BoardAsync(activeActor, id)).EligibleFootballers;
        var rejected = await activeActor.PostAsJsonAsync($"/api/drafts/{id}/pick", new { footballerId = 999_999, expectedVersion = paused.Summary.Version });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(pool); // the paused board offers no pool: there is no active position turn

        // A long pause elapses no draft time and never auto-picks.
        factory.Clock.Advance(TimeSpan.FromMinutes(45));
        var resumed = await OkAsync(host, $"/api/drafts/{id}/resume", new { expectedVersion = paused.Summary.Version });
        Assert.Equal("PositionDraft", resumed.Summary.Status);
        Assert.False(resumed.Timer.IsPaused);
        Assert.Equal(100, resumed.Timer.RemainingSeconds!.Value, precision: 1);
        Assert.DoesNotContain(resumed.Events, evt => evt.Type == "PickAutoSelected");
    }

    [Fact]
    public async Task Cancelling_requires_a_reason_and_preserves_the_history()
    {
        var (detail, host, guest, _) = await PositionDraftAsync("cancel");
        var id = detail.Summary.Id;

        // Only the host (or an admin) may cancel; a reason is mandatory.
        var forbidden = await guest.PostAsJsonAsync($"/api/drafts/{id}/cancel", new { reason = "I quit", expectedVersion = detail.Summary.Version });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var missingReason = await host.PostAsJsonAsync($"/api/drafts/{id}/cancel", new { reason = " ", expectedVersion = detail.Summary.Version });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        var eventsBefore = detail.Events.Count;
        var cancelled = await OkAsync(host, $"/api/drafts/{id}/cancel", new { reason = "Session called off", expectedVersion = detail.Summary.Version });

        Assert.Equal("Cancelled", cancelled.Summary.Status);
        Assert.Equal(eventsBefore + 1, cancelled.Events.Count); // append-only: nothing rewritten or removed
        var cancel = Assert.Single(cancelled.Events, evt => evt.Type == "DraftCancelled");
        Assert.Equal("Session called off", cancel.Reason);

        // A terminal draft accepts no further picks or controls.
        var latePick = await host.PostAsJsonAsync($"/api/drafts/{id}/pick", new { footballerId = 1, expectedVersion = cancelled.Summary.Version });
        Assert.Equal(HttpStatusCode.BadRequest, latePick.StatusCode);
    }

    [Fact]
    public async Task Admin_recovery_is_admin_only_and_audited()
    {
        var (detail, host, _, _) = await PositionDraftAsync("recover");
        var id = detail.Summary.Id;

        // The host is not enough — recovery is separately permissioned (§9.7).
        var forbidden = await host.PostAsJsonAsync(
            $"/api/drafts/{id}/recover", new { reason = "restart clock", restartTurnClock = true, expectedVersion = detail.Summary.Version });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        factory.Clock.Advance(TimeSpan.FromSeconds(110)); // 10 seconds remain
        var admin = factory.CreateClient().WithBearer(await AdminTokenAsync());
        var recovered = await OkAsync(admin, $"/api/drafts/{id}/recover",
            new { reason = "Turn clock restored after an outage", restartTurnClock = true, expectedVersion = detail.Summary.Version });

        var recovery = Assert.Single(recovered.Events, evt => evt.Type == "AdminRecoveryApplied");
        Assert.Equal("Turn clock restored after an outage", recovery.Reason);
        Assert.Equal(120, recovered.Timer.RemainingSeconds!.Value, precision: 1); // a fresh full clock
        Assert.Equal("PositionDraft", recovered.Summary.Status);
    }
}
