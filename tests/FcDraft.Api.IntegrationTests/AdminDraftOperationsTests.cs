using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Drives the PR-21 admin draft operations over real HTTP: an admin who is neither host nor
/// participant can inspect any draft and pause/resume/cancel it on the PR-16 commands — version
/// checked (409 on stale), reason-captured, and every action appended to the immutable event trail
/// with the admin as the attributed actor. Recovery is compensating: the pre-existing history is
/// byte-for-byte untouched by later operations.
/// </summary>
public sealed class AdminDraftOperationsTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private const string StrongPassword = "Strong@2026Pass";

    private async Task<(HttpClient Client, Guid UserId)> AdminAsync()
    {
        var login = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return (factory.CreateClient().WithBearer(login.AccessToken), login.User.Id);
    }

    private async Task<(HttpClient Client, Guid UserId)> HostAsync()
    {
        var login = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        return (factory.CreateClient().WithBearer(login.AccessToken), login.User.Id);
    }

    private async Task<(Guid UserId, HttpClient Client)> ActivePlayerAsync(string email, string name)
    {
        var (admin, _) = await AdminAsync();
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

    /// <summary>Drives a fresh 1v1 draft into the live position draft (the pausable state).</summary>
    private async Task<(LobbyDetail Detail, HttpClient Host, HttpClient Guest, Guid GuestId)> PositionDraftAsync(string tag)
    {
        var (host, hostId) = await HostAsync();
        var (guestId, guest) = await ActivePlayerAsync($"ops.{tag}@draftroom.test", $"Ops {tag}");
        var clientByUser = new Dictionary<Guid, HttpClient> { [hostId] = host, [guestId] = guest };

        var create = await host.PostAsJsonAsync("/api/drafts", new { name = $"Ops Lobby {tag}", format = "1v1", inviteUserIds = new[] { guestId } });
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
        return (detail, host, guest, guestId);
    }

    [Fact]
    public async Task An_admin_can_inspect_pause_and_resume_a_draft_they_do_not_participate_in()
    {
        var (detail, _, _, _) = await PositionDraftAsync("pauseresume");
        var (admin, adminId) = await AdminAsync();
        var id = detail.Summary.Id;

        // Inspect: the admin-scoped read returns the full snapshot with the event history.
        var inspected = (await admin.GetFromJsonAsync<LobbyDetail>($"/api/drafts/{id}"))!;
        Assert.NotEmpty(inspected.Events);
        var historyBeforePause = inspected.Events.Select(e => (e.Sequence, e.Type, e.Version)).ToArray();

        // Pause with a required reason.
        var paused = await OkAsync(admin, $"/api/drafts/{id}/pause",
            new { reason = "Connection dispute on Team 2", expectedVersion = inspected.Summary.Version });
        Assert.Equal("Paused", paused.Summary.Status);
        var pauseEvent = paused.Events.Last();
        Assert.Equal("DraftPaused", pauseEvent.Type);
        Assert.Equal(adminId, pauseEvent.ActorUserId);          // attributable (§17.8)
        Assert.Equal("Connection dispute on Team 2", pauseEvent.Reason);

        // Resume returns to the round it paused from and appends — never edits.
        var resumed = await OkAsync(admin, $"/api/drafts/{id}/resume",
            new { expectedVersion = paused.Summary.Version });
        Assert.Equal("PositionDraft", resumed.Summary.Status);
        Assert.Equal("DraftResumed", resumed.Events.Last().Type);
        Assert.Equal(adminId, resumed.Events.Last().ActorUserId);

        // Compensating history: everything that existed before the pause is untouched.
        var historyAfter = resumed.Events.Select(e => (e.Sequence, e.Type, e.Version)).ToArray();
        Assert.Equal(historyBeforePause, historyAfter.Take(historyBeforePause.Length).ToArray());
        Assert.Equal(historyBeforePause.Length + 2, historyAfter.Length);

        // The §9.10 audit view sees the same trail, with the admin's name resolved.
        var audit = (await admin.GetFromJsonAsync<List<DraftAuditRow>>(
            $"/api/admin/audit/draft-events?draftId={id}&type=DraftPaused"))!;
        var row = Assert.Single(audit);
        Assert.Equal(adminId, row.ActorUserId);
        Assert.NotNull(row.ActorName);
        Assert.Equal("Connection dispute on Team 2", row.Reason);
    }

    [Fact]
    public async Task A_stale_version_is_a_409_and_a_blank_reason_is_a_400()
    {
        var (detail, _, _, _) = await PositionDraftAsync("staleversion");
        var (admin, _) = await AdminAsync();
        var id = detail.Summary.Id;

        var stale = await admin.PostAsJsonAsync($"/api/drafts/{id}/pause",
            new { reason = "Too late", expectedVersion = detail.Summary.Version - 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var blank = await admin.PostAsJsonAsync($"/api/drafts/{id}/pause",
            new { reason = "", expectedVersion = detail.Summary.Version });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task An_admin_cancellation_captures_the_reason_and_notifies_participants()
    {
        var (detail, _, guest, _) = await PositionDraftAsync("cancel");
        var (admin, adminId) = await AdminAsync();
        var id = detail.Summary.Id;

        var cancelled = await OkAsync(admin, $"/api/drafts/{id}/cancel",
            new { reason = "Restarting after a rules dispute", expectedVersion = detail.Summary.Version });
        Assert.Equal("Cancelled", cancelled.Summary.Status);
        var cancelEvent = cancelled.Events.Last();
        Assert.Equal("DraftCancelled", cancelEvent.Type);
        Assert.Equal(adminId, cancelEvent.ActorUserId);
        Assert.Equal("Restarting after a rules dispute", cancelEvent.Reason);

        // The PR-20 pipeline told every participant why.
        var inbox = (await guest.GetFromJsonAsync<NotificationsPage>("/api/me/notifications"))!;
        Assert.Contains(inbox.Items, row =>
            row.Type == "draft.cancelled" && row.DraftId == id && row.Body.Contains("rules dispute"));

        // Terminal: nothing else can act on it, and the history stays queryable.
        var afterCancel = await admin.PostAsJsonAsync($"/api/drafts/{id}/pause",
            new { reason = "Too late", expectedVersion = cancelled.Summary.Version });
        Assert.Equal(HttpStatusCode.BadRequest, afterCancel.StatusCode);

        var audit = (await admin.GetFromJsonAsync<List<DraftAuditRow>>(
            $"/api/admin/audit/draft-events?draftId={id}"))!;
        Assert.Contains(audit, row => row.Type == "DraftCancelled" && row.ActorUserId == adminId);
    }

    [Fact]
    public async Task The_draft_event_audit_filters_by_draft_actor_and_date()
    {
        var (detail, _, _, guestId) = await PositionDraftAsync("filters");
        var (admin, _) = await AdminAsync();
        var id = detail.Summary.Id;

        var byDraft = (await admin.GetFromJsonAsync<List<DraftAuditRow>>(
            $"/api/admin/audit/draft-events?draftId={id}"))!;
        Assert.NotEmpty(byDraft);
        Assert.All(byDraft, row => Assert.Equal(id, row.DraftId));
        // Newest first: sequences descend.
        Assert.Equal(byDraft.Select(row => row.Sequence).OrderByDescending(s => s), byDraft.Select(row => row.Sequence));

        var byActor = (await admin.GetFromJsonAsync<List<DraftAuditRow>>(
            $"/api/admin/audit/draft-events?draftId={id}&actorUserId={guestId}"))!;
        Assert.NotEmpty(byActor);
        Assert.All(byActor, row => Assert.Equal(guestId, row.ActorUserId));

        var future = (await admin.GetFromJsonAsync<List<DraftAuditRow>>(
            $"/api/admin/audit/draft-events?draftId={id}&from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}"))!;
        Assert.Empty(future);

        var badType = await admin.GetAsync($"/api/admin/audit/draft-events?type=NotAThing");
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
    }
}
