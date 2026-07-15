using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Covers the server-backed player explorer (PR-08) over the in-memory bundled dataset: pagination,
/// position filtering, search, filter options, the 75+ eligibility boundary, and auth.
/// </summary>
public sealed class PlayerExplorerTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private sealed record PlayerCard(int Id, string Name, int Overall, string Position, List<string> AlternatePositions);
    private sealed record SearchResult(List<PlayerCard> Items, int Page, int PageSize, int Total, int TotalPages, string DatasetLabel);
    private sealed record FilterOptions(List<string> Positions, List<string> Leagues, List<string> Nations);

    private async Task<HttpClient> PlayerAsync()
    {
        var session = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        return factory.CreateClient().WithBearer(session.AccessToken);
    }

    [Fact]
    public async Task Explorer_requires_authentication()
    {
        var response = await factory.CreateClient().GetAsync("/api/players");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_pages_and_only_returns_eligible_players()
    {
        var client = await PlayerAsync();

        var result = await client.GetFromJsonAsync<SearchResult>("/api/players?page=1&pageSize=10");
        Assert.Equal(10, result!.Items.Count);
        Assert.True(result.Total > 1000);
        Assert.True(result.TotalPages > 1);
        Assert.All(result.Items, player => Assert.True(player.Overall >= 75));
    }

    [Fact]
    public async Task Position_filter_only_returns_players_who_fill_the_position()
    {
        var client = await PlayerAsync();

        var result = await client.GetFromJsonAsync<SearchResult>("/api/players?position=GK&pageSize=20");
        Assert.NotEmpty(result!.Items);
        Assert.All(result.Items, player =>
            Assert.True(player.Position == "GK" || player.AlternatePositions.Contains("GK")));
    }

    [Fact]
    public async Task Search_matches_a_known_player_by_name()
    {
        var client = await PlayerAsync();

        var result = await client.GetFromJsonAsync<SearchResult>("/api/players?search=Mbapp");
        Assert.Contains(result!.Items, player => player.Name.Contains("Mbapp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Filter_options_expose_positions_from_the_dataset()
    {
        var client = await PlayerAsync();

        var options = await client.GetFromJsonAsync<FilterOptions>("/api/players/filters");
        Assert.Contains("ST", options!.Positions);
        Assert.NotEmpty(options.Leagues);
        Assert.NotEmpty(options.Nations);
    }
}
