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
    public async Task Formation_catalogue_is_listed_and_the_active_default_can_be_switched()
    {
        var admin = await AdminAsync();
        var templates = await admin.GetFromJsonAsync<List<TemplateSummary>>("/api/admin/roster-templates");

        // The whole FIFA formation catalogue is selectable per lobby, not just the MVP 4-3-3.
        Assert.True(templates!.Count > 1, "the formation catalogue should list more than one formation");
        Assert.Contains(templates, template => template.Name == "MVP 4-3-3");
        Assert.Contains(templates, template => template.Name == "4-4-2");
        Assert.Contains(templates, template => template.Name == "3-5-2");

        var mvp = templates.Single(template => template.Name == "MVP 4-3-3");
        var other = templates.First(template => template.Id != mvp.Id);
        try
        {
            // Switching the active formation works in the in-memory foundation (no database needed).
            var activate = await admin.PostAsync($"/api/admin/roster-templates/{other.Id}/activate", null);
            Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

            var active = await admin.GetFromJsonAsync<TemplateDetail>("/api/admin/roster-templates/active");
            Assert.Equal(other.Id, active!.Summary.Id);
        }
        finally
        {
            // Reset so sibling tests still see the MVP 4-3-3 as the active default (shared singleton).
            await admin.PostAsync($"/api/admin/roster-templates/{mvp.Id}/activate", null);
        }
    }

    [Fact]
    public async Task Creating_a_custom_template_requires_the_database()
    {
        var admin = await AdminAsync();
        var create = await admin.PostAsJsonAsync("/api/admin/roster-templates", new
        {
            name = "Custom shape",
            pickTimerSeconds = 90,
            slots = new[] { new { order = 0, slotType = "Held", position = (string?)null, label = "Held player" } },
        });

        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
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
