using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FcDraft.Api.DatabaseTests;

/// <summary>Deterministic accounts seeded into the PostgreSQL database when SeedDevelopmentAccounts is on.</summary>
public static class SeededAccounts
{
    public const string AdminEmail = "admin@draftroom.dev";
    public const string AdminPassword = "DraftAdmin@2026";
    public const string PlayerEmail = "player@draftroom.dev";
    public const string PlayerPassword = "Player@2026";
}

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, bool MustChangePassword, UserSummary User);
public sealed record UserSummary(Guid Id, string DisplayName, string Email, string Role);
public sealed record HealthResponse(string Status, string Service, Dictionary<string, string> Checks);

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
