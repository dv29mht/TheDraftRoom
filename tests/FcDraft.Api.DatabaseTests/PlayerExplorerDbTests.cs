using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Query-boundary proof for PR-08 against a real PostgreSQL server: the explorer serves only eligible
/// (75+, Kick Off, active) footballers from the active dataset version. Below-threshold and
/// non-active-version content never appears. Skips cleanly when Docker is unavailable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PlayerExplorerDbTests(PostgresFixture fixture)
{
    private sealed record PlayerCard(int Id, string Name, int Overall);
    private sealed record SearchResult(List<PlayerCard> Items, int Total, string DatasetLabel);
    private sealed record ReportRow(Guid VersionId, int RowsImported, int ErrorCount);

    [SkippableFact]
    public async Task Explorer_excludes_below_threshold_and_non_active_content()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        var client = factory.CreateClient().WithBearer(admin.AccessToken);

        // Import and activate a tiny dataset: one eligible player, one below the 75+ threshold.
        var upload = await client.PostAsJsonAsync("/api/admin/datasets/upload", new
        {
            version = "TEST-BOUNDARY",
            source = "unit-test",
            players = new object[]
            {
                new { id = 700001, name = "Eligible Striker", overall = 88, position = "ST", alternatePositions = new[] { "CF" }, club = "Boundary FC", league = "Boundary League", nation = "Testland" },
                new { id = 700002, name = "Sub Threshold Mid", overall = 70, position = "CM", alternatePositions = Array.Empty<string>(), club = "Boundary FC", league = "Boundary League", nation = "Testland" },
            }
        });
        var report = (await upload.Content.ReadFromJsonAsync<ReportRow>())!;
        Assert.Equal(2, report.RowsImported);
        Assert.Equal(0, report.ErrorCount);

        var activate = await client.PostAsync($"/api/admin/datasets/{report.VersionId}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        // The explorer now serves ONLY the active version, and within it only the eligible player.
        var result = await client.GetFromJsonAsync<SearchResult>("/api/players?pageSize=50");
        Assert.Equal("TEST-BOUNDARY", result!.DatasetLabel);
        Assert.Equal(1, result.Total);
        Assert.Contains(result.Items, player => player.Id == 700001);
        Assert.DoesNotContain(result.Items, player => player.Id == 700002); // below-threshold excluded
        Assert.All(result.Items, player => Assert.True(player.Overall >= 75));

        // The archived bundled roster (thousands of players) is no longer visible either.
        Assert.Single(result.Items);

        // Detail lookup honours the same boundary: the below-threshold player is not found.
        var missing = await client.GetAsync("/api/players/700002");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
