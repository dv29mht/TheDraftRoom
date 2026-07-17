using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// The PR-24 admin Overview endpoint (§8.2) over real HTTP against the in-memory host: it is admin-only,
/// and it summarizes users, drafts, and engagement plus derives attention alerts. Counts use <c>&gt;=</c>
/// because the class-fixture host shares its in-memory stores across the two tests.
/// </summary>
public sealed class AdminOverviewTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private sealed record OverviewUsers(int Total, int Activated, int AwaitingActivation, int Invited);
    private sealed record OverviewDrafts(
        int Total, int Live, int Completed, int Cancelled, int OneVOne, int TwoVTwo,
        Dictionary<string, int> ByStatus);
    private sealed record OverviewEngagement(
        int Created, int Started, int Completed, double LobbyToStartRate, double CompletionRate,
        int PicksAccepted, int AutoPicks, double AutoPickRate);
    private sealed record OverviewEmail(int Pending, int Sent, int Failed);
    private sealed record OverviewAlert(string Severity, string Message);
    private sealed record Overview(
        OverviewUsers Users, OverviewDrafts Drafts, OverviewEngagement Engagement, OverviewEmail Email,
        List<OverviewAlert> Alerts, DateTimeOffset GeneratedAt);

    private async Task<HttpClient> AdminAsync()
    {
        var login = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return factory.CreateClient().WithBearer(login.AccessToken);
    }

    [Fact]
    public async Task Overview_is_admin_only()
    {
        var player = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        var response = await factory.CreateClient().WithBearer(player.AccessToken).GetAsync("/api/admin/overview");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Overview_summarizes_users_drafts_engagement_and_alerts()
    {
        var admin = await AdminAsync();

        // A freshly invited account still must set a password → awaiting-activation + an info alert.
        var email = $"overview-{Guid.NewGuid():N}@draftroom.test";
        (await admin.PostAsJsonAsync("/api/users", new { email, displayName = "Overview Probe" }))
            .EnsureSuccessStatusCode();

        // A lobby → drafts.total, the Lobby status bucket, and the DraftCreated engagement event.
        var hostLogin = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        var host = factory.CreateClient().WithBearer(hostLogin.AccessToken);
        (await host.PostAsJsonAsync("/api/drafts",
            new { name = "Overview Lobby", format = "1v1", inviteUserIds = Array.Empty<Guid>() }))
            .EnsureSuccessStatusCode();

        var overview = await admin.GetFromJsonAsync<Overview>("/api/admin/overview");

        Assert.NotNull(overview);
        Assert.True(overview!.Users.Total >= 3);                 // 2 seeded + the probe
        Assert.True(overview.Users.AwaitingActivation >= 1);     // the probe hasn't activated
        Assert.Contains(overview.Alerts, alert => alert.Severity == "info" && alert.Message.Contains("password"));
        Assert.True(overview.Drafts.Total >= 1);
        Assert.True(overview.Drafts.ByStatus.GetValueOrDefault("Lobby") >= 1);
        Assert.True(overview.Drafts.OneVOne >= 1);
        Assert.True(overview.Engagement.Created >= 1);
        Assert.InRange(overview.Engagement.LobbyToStartRate, 0d, 1d);
        Assert.True(overview.GeneratedAt > DateTimeOffset.UnixEpoch);
    }
}
