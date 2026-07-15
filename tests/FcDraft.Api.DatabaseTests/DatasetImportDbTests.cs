using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Proves the PR-07 versioned dataset import against a real PostgreSQL server: the bundled dataset is
/// seeded and active, a new version can be imported as a draft and activated (archiving — never
/// deleting — the previous active version), and validation records per-row issues that block
/// activation. Skips cleanly when Docker is unavailable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatasetImportDbTests(PostgresFixture fixture)
{
    private sealed record VersionRow(
        Guid Id, string Label, string Source, string Status,
        int FootballerCount, int ClubCount, int ErrorCount, int WarningCount);

    private sealed record IssueRow(string Severity, int Row, int? ExternalId, string? Field, string Message);

    private sealed record DetailRow(VersionRow Summary, List<IssueRow> Issues);

    private sealed record ReportRow(
        Guid VersionId, string Label, string Status, int RowsTotal, int RowsImported,
        int ClubCount, int ErrorCount, int WarningCount, List<IssueRow> Issues);

    private static async Task<HttpClient> AdminAsync(PostgresApiFactory factory)
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return factory.CreateClient().WithBearer(admin.AccessToken);
    }

    [SkippableFact]
    public async Task Bundled_dataset_is_seeded_with_the_full_roster()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        var admin = await AdminAsync(factory);

        // The bundled dataset is imported on first boot. Other tests in this shared-database collection
        // may activate their own version, so assert it was seeded (exists, clean, full roster) rather
        // than that it is still the active one. Exactly one version is active at any time.
        var versions = (await admin.GetFromJsonAsync<List<VersionRow>>("/api/admin/datasets"))!;
        Assert.Contains(versions, version => version.FootballerCount > 1000 && version.ClubCount > 0 && version.ErrorCount == 0);
        Assert.Single(versions, version => version.Status == "Active");
    }

    [SkippableFact]
    public async Task Activating_a_new_version_archives_the_previous_active_and_retains_both()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        var admin = await AdminAsync(factory);

        var previouslyActive = (await admin.GetFromJsonAsync<List<VersionRow>>("/api/admin/datasets"))!
            .Single(version => version.Status == "Active");

        // Upload a small, clean dataset as a new draft.
        var upload = await admin.PostAsJsonAsync("/api/admin/datasets/upload", new
        {
            version = "TEST-CLEAN",
            source = "unit-test",
            players = new object[]
            {
                new { id = 900001, name = "Test Striker", overall = 88, position = "ST", alternatePositions = new[] { "CF" }, club = "Test United", league = "Test League", nation = "Testland" },
                new { id = 900002, name = "Test Keeper", overall = 84, position = "GK", alternatePositions = Array.Empty<string>(), club = "Test United", league = "Test League", nation = "Testland" },
            }
        });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var report = (await upload.Content.ReadFromJsonAsync<ReportRow>())!;
        Assert.Equal(2, report.RowsImported);
        Assert.Equal(0, report.ErrorCount);
        Assert.Equal("Draft", report.Status);

        // Activate it.
        var activate = await admin.PostAsync($"/api/admin/datasets/{report.VersionId}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        // The previous active version is archived (retained), and the new one is active.
        var after = (await admin.GetFromJsonAsync<List<VersionRow>>("/api/admin/datasets"))!;
        Assert.Equal("Active", after.Single(version => version.Id == report.VersionId).Status);
        Assert.Equal("Archived", after.Single(version => version.Id == previouslyActive.Id).Status);
        Assert.Single(after, version => version.Status == "Active");
    }

    [SkippableFact]
    public async Task Upload_records_validation_issues_and_blocks_activation_on_errors()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        var admin = await AdminAsync(factory);

        var upload = await admin.PostAsJsonAsync("/api/admin/datasets/upload", new
        {
            version = "TEST-BAD",
            source = "unit-test",
            players = new object[]
            {
                new { id = 800001, name = "Valid One", overall = 88, position = "ST", alternatePositions = new[] { "LW" }, club = "Bad FC", league = "Bad League", nation = "Nowhere" },
                new { id = 0, name = "Missing Id", overall = 85, position = "CM" },
                new { id = 800001, name = "Duplicate Id", overall = 84, position = "CB" },
                new { id = 800002, name = "", overall = 83, position = "GK" },
                new { id = 800003, name = "Bad Position", overall = 82, position = "XX" },
                new { id = 800004, name = "Below Threshold", overall = 70, position = "RW" },
            }
        });
        var report = (await upload.Content.ReadFromJsonAsync<ReportRow>())!;

        Assert.Equal(6, report.RowsTotal);
        Assert.Equal(2, report.RowsImported); // the valid one and the below-threshold (imported but inactive)
        Assert.Equal(4, report.ErrorCount);   // missing id, duplicate id, missing name, invalid position
        Assert.True(report.WarningCount >= 1);

        // Issues are inspectable on the version detail.
        var detail = await admin.GetFromJsonAsync<DetailRow>($"/api/admin/datasets/{report.VersionId}");
        Assert.Contains(detail!.Issues, issue => issue.Severity == "Error" && issue.Field == "Position");

        // A version with errors cannot be activated.
        var activate = await admin.PostAsync($"/api/admin/datasets/{report.VersionId}/activate", null);
        Assert.Equal(HttpStatusCode.Conflict, activate.StatusCode);
    }
}
