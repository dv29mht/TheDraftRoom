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
public sealed record LobbyTeam(Guid Id, string Name, int? SpinnerRank, Guid? SelectedClubId, string? SelectedClubName, List<Guid> MemberUserIds);
public sealed record LobbyPick(
    Guid TeamId, int SlotOrder, int FootballerId, string FootballerName, int FootballerOverall, string? FootballerPosition, Guid? PickedByParticipantId);
public sealed record LobbyTurn(
    string Phase, Guid? ActiveTeamId, string? ActiveTeamName, List<Guid> ActiveTeamMemberUserIds,
    int? Round, string Direction, int? ActiveSlotOrder, string? ActiveSlotLabel, string? ActiveSlotPosition, bool SlotAcceptsAnyPosition);
public sealed record LobbyStartRequirements(
    int TeamCount, int MinTeams, int MaxTeams, int MembersPerTeam,
    bool AllPresent, bool AllAssigned, bool TeamsValid, bool AllReady,
    bool CanBeginReadyCheck, bool CanStart, List<string> BlockingReasons);
public sealed record LobbyTimer(
    bool IsTimed, bool IsPaused, int PickTimerSeconds, int WarningSeconds,
    DateTimeOffset? TurnStartedAt, DateTimeOffset? Deadline, double? RemainingSeconds, bool IsInWarning);
public sealed record LobbyEvent(int Sequence, string Type, string? FromStatus, string? ToStatus, int Version, Guid? ActorUserId, string? Reason);
public sealed record LobbyDetail(
    LobbySummary Summary, LobbyCapacity Capacity, LobbyStartRequirements StartRequirements,
    List<LobbyParticipant> Participants, List<LobbyTeam> Teams, List<LobbyPick> Picks, LobbyTurn Turn,
    LobbyTimer Timer, List<LobbyEvent> Events);
public sealed record TeamInput(string? Name, List<Guid> MemberUserIds);
public sealed record InvitableUser(Guid Id, string DisplayName, string Email);

// Draft board shapes (a subset — extra JSON fields are ignored).
public sealed record BoardClub(Guid Id, string Name, string League);
public sealed record BoardFootballer(int Id, string Name, int Overall, Guid ClubId, string ClubName, List<string> Positions);
public sealed record BoardDto(
    string Status, LobbyTurn Turn, LobbyTimer Timer, bool IsMyTurn, List<BoardClub> AvailableClubs, List<BoardFootballer> EligibleFootballers);

// The live-hub envelope every accepted mutation broadcasts (PR-17); Detail is null when the producer had
// only a summary — clients then refetch.
public sealed record DraftUpdateEnvelope(Guid DraftId, int Version, string EventType, LobbyDetail? Detail);

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
