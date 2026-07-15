using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Covers the dataset admin endpoints in the in-memory foundation: the bundled dataset is exposed
/// read-only, versioned import requires the database, and the endpoints are admin-only.
/// </summary>
public sealed class DatasetAdminTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private sealed record VersionRow(Guid Id, string Label, string Status, int FootballerCount, int ClubCount);

    [Fact]
    public async Task Bundled_dataset_is_exposed_read_only()
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        var client = factory.CreateClient().WithBearer(admin.AccessToken);

        var versions = await client.GetFromJsonAsync<List<VersionRow>>("/api/admin/datasets");
        var active = Assert.Single(versions!);
        Assert.Equal("Active", active.Status);
        Assert.True(active.FootballerCount > 1000);

        // Import/versioning is unavailable without the database.
        var import = await client.PostAsync("/api/admin/datasets/import-bundled", null);
        Assert.Equal(HttpStatusCode.Conflict, import.StatusCode);
    }

    [Fact]
    public async Task Dataset_endpoints_are_admin_only()
    {
        var anonymous = await factory.CreateClient().GetAsync("/api/admin/datasets");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var player = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        var forbidden = await factory.CreateClient().WithBearer(player.AccessToken).GetAsync("/api/admin/datasets");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
