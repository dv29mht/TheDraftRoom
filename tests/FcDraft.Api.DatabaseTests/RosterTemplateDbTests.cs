using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Proves PR-09 against a real PostgreSQL server: the locked 4-3-3 template is seeded and active, and
/// five-star club eligibility can be curated and queried from the active dataset. Skips cleanly when
/// Docker is unavailable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RosterTemplateDbTests(PostgresFixture fixture)
{
    private sealed record SlotRow(int Order, string SlotType, string? Position, string Label);
    private sealed record TemplateSummary(Guid Id, string Name, bool IsActive, int PickTimerSeconds, int SlotCount);
    private sealed record TemplateDetail(TemplateSummary Summary, List<SlotRow> Slots);
    private sealed record ClubRow(Guid Id, string Name, string League, bool IsFiveStarEligible);

    private static async Task<HttpClient> AdminAsync(PostgresApiFactory factory)
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return factory.CreateClient().WithBearer(admin.AccessToken);
    }

    [SkippableFact]
    public async Task Default_roster_template_is_seeded_and_active()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        var admin = await AdminAsync(factory);

        var active = await admin.GetFromJsonAsync<TemplateDetail>("/api/admin/roster-templates/active");
        Assert.Equal("MVP 4-3-3", active!.Summary.Name);
        Assert.True(active.Summary.IsActive);
        Assert.Equal(120, active.Summary.PickTimerSeconds);
        Assert.Equal(16, active.Slots.Count);
        Assert.Equal("Held", active.Slots.Single(slot => slot.Order == 0).SlotType);
        Assert.Equal("ST", active.Slots.Single(slot => slot.Order == 1).Position);
        Assert.Equal("GK", active.Slots.Single(slot => slot.Order == 11).Position);
    }

    [SkippableFact]
    public async Task Five_star_club_eligibility_can_be_curated_and_queried()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        var admin = await AdminAsync(factory);

        // Pick any club from the active dataset (whichever version is currently active).
        var clubs = await admin.GetFromJsonAsync<List<ClubRow>>("/api/admin/clubs");
        var club = clubs!.First();

        // Mark it five-star and confirm it appears in the eligible list.
        var marked = await admin.PutAsJsonAsync($"/api/admin/clubs/{club.Id}/five-star", new { eligible = true });
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);

        var eligible = await admin.GetFromJsonAsync<List<ClubRow>>("/api/admin/clubs/eligible");
        Assert.Contains(eligible!, candidate => candidate.Id == club.Id && candidate.IsFiveStarEligible);

        // Un-mark it and confirm it drops out.
        await admin.PutAsJsonAsync($"/api/admin/clubs/{club.Id}/five-star", new { eligible = false });
        var afterRemoval = await admin.GetFromJsonAsync<List<ClubRow>>("/api/admin/clubs/eligible");
        Assert.DoesNotContain(afterRemoval!, candidate => candidate.Id == club.Id);
    }
}
