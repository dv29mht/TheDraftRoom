using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FcDraft.Api.IntegrationTests;

/// <summary>Deterministic accounts seeded into the in-memory identity store on startup.</summary>
public static class SeededAccounts
{
    public const string AdminEmail = "mdevansh@gmail.com";
    public const string AdminPassword = "DraftAdmin@2026";
    public const string PlayerEmail = "player@draftroom.dev";
    public const string PlayerPassword = "Player@2026";
}

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, bool MustChangePassword, UserSummary User);
public sealed record UserSummary(Guid Id, string DisplayName, string Email, string Role);
public sealed record ManagedUser(Guid Id, string DisplayName, string Email, string Role, string Status, bool MustChangePassword);

// Lobby snapshot shapes for the draft endpoints (a subset of DraftDetail — extra JSON fields are ignored).
public sealed record LobbySummary(
    Guid Id, string Code, string Name, string Format, string Status, Guid HostUserId, int Version, int ParticipantCount);
public sealed record LobbyCapacity(
    int Min, int Max, bool RequiresEven, int ParticipantCount, int JoinedCount, int InvitedCount,
    bool MeetsMinimum, bool WithinMaximum, bool MeetsEven, bool CanLock);
public sealed record LobbyParticipant(
    Guid UserId, string? DisplayName, string? Email, bool IsHost, string? Seed, string Status, bool IsReady);
public sealed record LobbyDetail(
    LobbySummary Summary, LobbyCapacity Capacity, List<LobbyParticipant> Participants);
public sealed record InvitableUser(Guid Id, string DisplayName, string Email);

public static class ApiClientExtensions
{
    public static async Task<LoginResponse> LoginAsync(this HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
