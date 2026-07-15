using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Covers roster templates and club eligibility in the in-memory foundation: the locked 4-3-3
/// default is exposed read-only, management requires the database, and the endpoints are admin-only.
/// </summary>
public sealed class RosterTemplateTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private sealed record SlotRow(int Order, string SlotType, string? Position, string Label);
    private sealed record TemplateSummary(Guid Id, string Name, bool IsActive, int PickTimerSeconds, int SlotCount);
    private sealed record TemplateDetail(TemplateSummary Summary, List<SlotRow> Slots);

    private async Task<HttpClient> AdminAsync()
    {
        var session = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return factory.CreateClient().WithBearer(session.AccessToken);
    }

    [Fact]
    public async Task Default_template_is_the_locked_4_3_3()
    {
        var admin = await AdminAsync();

        var active = await admin.GetFromJsonAsync<TemplateDetail>("/api/admin/roster-templates/active");
        Assert.Equal("MVP 4-3-3", active!.Summary.Name);
        Assert.Equal(120, active.Summary.PickTimerSeconds);
        Assert.Equal(16, active.Slots.Count);

        Assert.Equal("Held", active.Slots[0].SlotType);
        Assert.Equal("ST", active.Slots[1].Position);
        Assert.Equal("GK", active.Slots.Single(slot => slot.Order == 11).Position);
        Assert.Equal(4, active.Slots.Count(slot => slot.SlotType == "FlexBench"));
    }

    [Fact]
    public async Task Template_management_requires_the_database()
    {
        var admin = await AdminAsync();
        var templates = await admin.GetFromJsonAsync<List<TemplateSummary>>("/api/admin/roster-templates");
        var template = Assert.Single(templates!);

        var activate = await admin.PostAsync($"/api/admin/roster-templates/{template.Id}/activate", null);
        Assert.Equal(HttpStatusCode.Conflict, activate.StatusCode);
    }

    [Fact]
    public async Task Roster_and_club_endpoints_are_admin_only()
    {
        var anonymous = await factory.CreateClient().GetAsync("/api/admin/roster-templates");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var player = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        var forbidden = await factory.CreateClient().WithBearer(player.AccessToken).GetAsync("/api/admin/clubs/eligible");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
