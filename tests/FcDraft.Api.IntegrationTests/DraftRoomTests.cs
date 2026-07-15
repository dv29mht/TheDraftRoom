using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

public sealed class DraftRoomTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private async Task<HttpClient> PlayerClientAsync()
    {
        var session = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        return factory.CreateClient().WithBearer(session.AccessToken);
    }

    [Fact]
    public async Task An_active_player_can_create_and_list_a_draft_room()
    {
        var player = await PlayerClientAsync();

        var create = await player.PostAsJsonAsync("/api/draft-rooms", new { name = "Friday Night Draft", format = "2v2" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var room = (await create.Content.ReadFromJsonAsync<RoomResponse>())!;
        Assert.Equal("Friday Night Draft", room.Name);
        Assert.Equal("2v2", room.Format);
        Assert.False(string.IsNullOrWhiteSpace(room.Code));

        var list = await (await PlayerClientAsync()).GetFromJsonAsync<List<RoomResponse>>("/api/draft-rooms");
        Assert.Contains(list!, r => r.Id == room.Id);
    }

    [Fact]
    public async Task Creating_a_room_with_an_invalid_format_returns_400()
    {
        var player = await PlayerClientAsync();

        var response = await player.PostAsJsonAsync("/api/draft-rooms", new { name = "Bad Format", format = "5v5" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_room_without_a_token_returns_401()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/draft-rooms", new { name = "Anon Lobby", format = "1v1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
