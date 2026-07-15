using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Proves the PR-04 definition of done against a real PostgreSQL server: paging and counting run in
/// the database, deactivation retains the account (no hard delete), the delete route is gone, and
/// the optional avatar/preferred-team-name profile fields persist across an API restart. Each test
/// skips cleanly when Docker is unavailable (see <see cref="PostgresFixture"/>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UserDirectoryTests(PostgresFixture fixture)
{
    private sealed record DirectoryUser(
        Guid Id,
        string DisplayName,
        string Email,
        string Role,
        string Status,
        bool MustChangePassword,
        string? AvatarUrl,
        string? PreferredTeamName);

    private sealed record PagedUsers(
        IReadOnlyList<DirectoryUser> Items,
        int Page,
        int PageSize,
        int Total,
        int TotalPages,
        int InvitedCount,
        int ActivatedCount);

    [SkippableFact]
    public async Task Directory_pages_and_searches_in_the_database()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        var client = factory.CreateClient().WithBearer(admin.AccessToken);

        var stamp = Guid.NewGuid().ToString("N")[..8];
        for (var index = 0; index < 12; index++)
        {
            var create = await client.PostAsJsonAsync("/api/users", new
            {
                email = $"page-{stamp}-{index:00}@draftroom.test",
                displayName = $"Page {stamp} {index:00}",
            });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }

        // Search narrows to the 12 just created; page size 10 => 2 pages, second page has 2.
        var firstPage = await client.GetFromJsonAsync<PagedUsers>(
            $"/api/users?search=Page%20{stamp}&page=1&pageSize=10");
        Assert.Equal(12, firstPage!.Total);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(10, firstPage.Items.Count);

        var secondPage = await client.GetFromJsonAsync<PagedUsers>(
            $"/api/users?search=Page%20{stamp}&page=2&pageSize=10");
        Assert.Equal(2, secondPage!.Items.Count);
        Assert.Equal(2, secondPage.Page);

        // Items are ordered by display name and do not repeat across pages.
        var combined = firstPage.Items.Concat(secondPage.Items).Select(user => user.Email).ToArray();
        Assert.Equal(12, combined.Distinct().Count());
    }

    [SkippableFact]
    public async Task Deactivation_retains_the_account_and_there_is_no_delete_route()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        var client = factory.CreateClient().WithBearer(admin.AccessToken);

        var email = $"retain-{Guid.NewGuid():N}@draftroom.test";
        var created = (await (await client.PostAsJsonAsync("/api/users", new { email, displayName = "Retain Me" }))
            .Content.ReadFromJsonAsync<DirectoryUser>())!;

        var deactivate = await client.PostAsync($"/api/users/{created.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        // The account is retained (still searchable), just marked deactivated.
        var page = await client.GetFromJsonAsync<PagedUsers>($"/api/users?search={Uri.EscapeDataString(email)}");
        var retained = Assert.Single(page!.Items);
        Assert.Equal("deactivated", retained.Status);

        // Hard delete was removed in PR-04: no DELETE handler exists, so the SPA fallback answers
        // the unknown /api route with 404 rather than performing a delete.
        var delete = await client.DeleteAsync($"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [SkippableFact]
    public async Task Optional_profile_fields_persist_across_a_restart()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        var email = $"profile-{Guid.NewGuid():N}@draftroom.test";
        Guid userId;

        await using (var first = new PostgresApiFactory(fixture.ConnectionString!))
        {
            var admin = await first.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
            var client = first.CreateClient().WithBearer(admin.AccessToken);

            var created = (await (await client.PostAsJsonAsync("/api/users", new { email, displayName = "Profile Player" }))
                .Content.ReadFromJsonAsync<DirectoryUser>())!;
            userId = created.Id;

            var updated = await client.PutAsJsonAsync($"/api/users/{userId}", new
            {
                displayName = "Profile Player",
                email,
                role = "player",
                avatarUrl = "https://cdn.example/avatar.png",
                preferredTeamName = "The Galácticos",
            });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }

        // A fresh host on the same database must still return the persisted profile fields.
        await using var second = new PostgresApiFactory(fixture.ConnectionString!);
        var admin2 = await second.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        var page = await second.CreateClient().WithBearer(admin2.AccessToken)
            .GetFromJsonAsync<PagedUsers>($"/api/users?search={Uri.EscapeDataString(email)}");

        var persisted = Assert.Single(page!.Items);
        Assert.Equal("https://cdn.example/avatar.png", persisted.AvatarUrl);
        Assert.Equal("The Galácticos", persisted.PreferredTeamName);
    }
}
