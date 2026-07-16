using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Drafts;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the PR-12 team-formation rules over the in-memory store: host-only Seed 1/Seed 2 assignment (2v2
/// only, team formation only); 1v1 auto solo-team projection and 2v2 host pairing (exactly one Seed 1 + one
/// Seed 2 per team, each participant on at most one team); self-service readiness; and the host-only ready
/// check that only opens once everyone is present and assigned to a valid team.
/// </summary>
public sealed class TeamFormationCommandHandlerTests
{
    private readonly InMemoryDraftStore _store = new();
    private readonly InMemoryTransactionRunner _runner = new();
    private readonly FakeRosterTemplateService _templates = new();
    private readonly FakeIdentityDirectory _identity = new();
    private readonly Guid _host;

    public TeamFormationCommandHandlerTests() => _host = _identity.Add("Host").Id;

    private CreateDraftCommandHandler Create() => new(_store, _templates, _identity, _runner, TestNotifiers.Lifecycle(_identity));
    private JoinDraftCommandHandler Join() => new(_store, _identity, _runner);
    private LockLobbyCommandHandler Lock() => new(_store, _identity, _runner);
    private AssignSeedCommandHandler AssignSeed() => new(_store, _identity, _runner);
    private FormTeamsCommandHandler FormTeams() => new(_store, _identity, _runner);
    private SetReadyCommandHandler SetReady() => new(_store, _identity, _runner);
    private BeginReadyCheckCommandHandler BeginReadyCheck() => new(_store, _identity, _runner);
    private ReopenTeamFormationCommandHandler ReopenTeams() => new(_store, _identity, _runner);

    private Guid[] AddPlayers(int count) =>
        Enumerable.Range(0, count).Select(index => _identity.Add($"Player{index}").Id).ToArray();

    /// <summary>Creates a lobby, joins every invitee, and locks it into team formation.</summary>
    private async Task<(DraftDetail Detail, Guid[] Guests)> LockedAsync(string format, params Guid[] guests)
    {
        var lobby = await Create().Handle(new CreateDraftCommand("Lobby", format, _host, null, guests), default);
        var version = lobby.Summary.Version;
        foreach (var guest in guests)
        {
            version = (await Join().Handle(new JoinDraftCommand(lobby.Summary.Id, version, guest), default)).Summary.Version;
        }

        var locked = await Lock().Handle(new LockLobbyCommand(lobby.Summary.Id, version, _host), default);
        return (locked, guests);
    }

    private static string? SeedOf(DraftDetail detail, Guid userId) =>
        detail.Participants.First(participant => participant.UserId == userId).Seed;

    private static bool IsReady(DraftDetail detail, Guid userId) =>
        detail.Participants.First(participant => participant.UserId == userId).IsReady;

    // --- Seeds --------------------------------------------------------------------------------------

    [Fact]
    public async Task Assigning_a_seed_in_a_2v2_records_it_and_appends_an_event()
    {
        var (locked, guests) = await LockedAsync("2v2", AddPlayers(3));

        var updated = await AssignSeed().Handle(
            new AssignSeedCommand(locked.Summary.Id, guests[0], "Seed1", locked.Summary.Version, _host), default);

        Assert.Equal("Seed1", SeedOf(updated, guests[0]));
        var stored = await _store.FindAsync(locked.Summary.Id, default);
        Assert.Contains(stored!.Events, evt => evt.Type == DraftEventType.ParticipantSeedAssigned);
    }

    [Fact]
    public async Task Assigning_a_seed_in_a_1v1_is_rejected()
    {
        var (locked, _) = await LockedAsync("1v1", AddPlayers(1));

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            AssignSeed().Handle(new AssignSeedCommand(locked.Summary.Id, _host, "Seed1", locked.Summary.Version, _host), default));
    }

    [Fact]
    public async Task Assigning_a_seed_by_a_non_host_is_forbidden()
    {
        var (locked, guests) = await LockedAsync("2v2", AddPlayers(3));

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            AssignSeed().Handle(new AssignSeedCommand(locked.Summary.Id, guests[0], "Seed1", locked.Summary.Version, guests[1]), default));
    }

    // --- Team formation ------------------------------------------------------------------------------

    [Fact]
    public async Task Forming_1v1_teams_projects_one_solo_team_per_participant()
    {
        var (locked, _) = await LockedAsync("1v1", AddPlayers(2)); // host + 2 = 3 solo teams

        var formed = await FormTeams().Handle(new FormTeamsCommand(locked.Summary.Id, null, locked.Summary.Version, _host), default);

        Assert.Equal(3, formed.Teams.Count);
        Assert.All(formed.Teams, team => Assert.Single(team.MemberUserIds));
        var stored = await _store.FindAsync(locked.Summary.Id, default);
        Assert.Contains(stored!.Events, evt => evt.Type == DraftEventType.TeamsFormed);
    }

    [Fact]
    public async Task Forming_a_valid_2v2_pairing_creates_two_valid_teams()
    {
        var (seeded, s1, s2) = await SeededTwoVsTwoAsync();

        var teams = new[]
        {
            new TeamFormationInput("Alpha", [s1[0], s2[0]]),
            new TeamFormationInput("Bravo", [s1[1], s2[1]]),
        };
        var formed = await FormTeams().Handle(new FormTeamsCommand(seeded.Summary.Id, teams, seeded.Summary.Version, _host), default);

        Assert.Equal(2, formed.Teams.Count);
        Assert.All(formed.Teams, team => Assert.Equal(2, team.MemberUserIds.Count));
        // Every participant is assigned exactly once.
        Assert.True(formed.StartRequirements.AllAssigned);
        Assert.True(formed.StartRequirements.TeamsValid);
    }

    [Fact]
    public async Task Forming_a_2v2_team_without_one_of_each_seed_is_rejected()
    {
        var (seeded, s1, s2) = await SeededTwoVsTwoAsync();

        // Two Seed 1s on one team violates the exactly-one-of-each rule.
        var teams = new[]
        {
            new TeamFormationInput("Bad", [s1[0], s1[1]]),
            new TeamFormationInput("Rest", [s2[0], s2[1]]),
        };

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            FormTeams().Handle(new FormTeamsCommand(seeded.Summary.Id, teams, seeded.Summary.Version, _host), default));
    }

    [Fact]
    public async Task Forming_teams_that_reuse_a_participant_is_rejected()
    {
        var (seeded, s1, s2) = await SeededTwoVsTwoAsync();

        // s2[0] appears on both teams.
        var teams = new[]
        {
            new TeamFormationInput("One", [s1[0], s2[0]]),
            new TeamFormationInput("Two", [s1[1], s2[0]]),
        };

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            FormTeams().Handle(new FormTeamsCommand(seeded.Summary.Id, teams, seeded.Summary.Version, _host), default));
    }

    [Fact]
    public async Task Forming_teams_by_a_non_host_is_forbidden()
    {
        var (locked, guests) = await LockedAsync("1v1", AddPlayers(1));

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            FormTeams().Handle(new FormTeamsCommand(locked.Summary.Id, null, locked.Summary.Version, guests[0]), default));
    }

    // --- Readiness + ready check ---------------------------------------------------------------------

    [Fact]
    public async Task A_participant_can_toggle_their_readiness()
    {
        var (locked, guests) = await LockedAsync("1v1", AddPlayers(1));
        var teams = await FormTeams().Handle(new FormTeamsCommand(locked.Summary.Id, null, locked.Summary.Version, _host), default);

        var ready = await SetReady().Handle(new SetReadyCommand(locked.Summary.Id, true, teams.Summary.Version, guests[0]), default);
        Assert.True(IsReady(ready, guests[0]));

        var cleared = await SetReady().Handle(new SetReadyCommand(locked.Summary.Id, false, ready.Summary.Version, guests[0]), default);
        Assert.False(IsReady(cleared, guests[0]));
    }

    [Fact]
    public async Task Re_forming_teams_clears_readiness()
    {
        var (locked, guests) = await LockedAsync("1v1", AddPlayers(1));
        var teams = await FormTeams().Handle(new FormTeamsCommand(locked.Summary.Id, null, locked.Summary.Version, _host), default);
        var ready = await SetReady().Handle(new SetReadyCommand(locked.Summary.Id, true, teams.Summary.Version, guests[0]), default);
        Assert.True(IsReady(ready, guests[0]));

        var reformed = await FormTeams().Handle(new FormTeamsCommand(locked.Summary.Id, null, ready.Summary.Version, _host), default);
        Assert.All(reformed.Participants, participant => Assert.False(participant.IsReady));
    }

    [Fact]
    public async Task Beginning_the_ready_check_before_everyone_is_assigned_is_rejected()
    {
        var (locked, _) = await LockedAsync("1v1", AddPlayers(1)); // no teams formed yet

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            BeginReadyCheck().Handle(new BeginReadyCheckCommand(locked.Summary.Id, locked.Summary.Version, _host), default));
    }

    [Fact]
    public async Task Beginning_the_ready_check_with_valid_teams_advances_the_state()
    {
        var (locked, _) = await LockedAsync("1v1", AddPlayers(1));
        var teams = await FormTeams().Handle(new FormTeamsCommand(locked.Summary.Id, null, locked.Summary.Version, _host), default);

        var ready = await BeginReadyCheck().Handle(new BeginReadyCheckCommand(locked.Summary.Id, teams.Summary.Version, _host), default);

        Assert.Equal("ReadyCheck", ready.Summary.Status);
        var stored = await _store.FindAsync(locked.Summary.Id, default);
        Assert.Contains(stored!.Events, evt => evt.Type == DraftEventType.ReadyCheckStarted);
    }

    [Fact]
    public async Task Reopening_team_formation_returns_to_team_formation_and_clears_readiness()
    {
        var (locked, guests) = await LockedAsync("1v1", AddPlayers(1));
        var teams = await FormTeams().Handle(new FormTeamsCommand(locked.Summary.Id, null, locked.Summary.Version, _host), default);
        var hostReady = await SetReady().Handle(new SetReadyCommand(locked.Summary.Id, true, teams.Summary.Version, _host), default);
        var guestReady = await SetReady().Handle(new SetReadyCommand(locked.Summary.Id, true, hostReady.Summary.Version, guests[0]), default);
        var readyCheck = await BeginReadyCheck().Handle(new BeginReadyCheckCommand(locked.Summary.Id, guestReady.Summary.Version, _host), default);

        var reopened = await ReopenTeams().Handle(new ReopenTeamFormationCommand(locked.Summary.Id, readyCheck.Summary.Version, _host), default);

        Assert.Equal("TeamFormation", reopened.Summary.Status);
        Assert.All(reopened.Participants, participant => Assert.False(participant.IsReady));
        var stored = await _store.FindAsync(locked.Summary.Id, default);
        Assert.Contains(stored!.Events, evt => evt.Type == DraftEventType.TeamFormationReopened);
    }

    // --- 2v2 helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// Locks a 4-player 2v2 into team formation and assigns two Seed 1s and two Seed 2s, returning the
    /// snapshot and the user ids grouped by seed.
    /// </summary>
    private async Task<(DraftDetail Detail, Guid[] Seed1, Guid[] Seed2)> SeededTwoVsTwoAsync()
    {
        var (locked, guests) = await LockedAsync("2v2", AddPlayers(3));
        var order = new[] { _host, guests[0], guests[1], guests[2] };
        var seeds = new[] { "Seed1", "Seed2", "Seed1", "Seed2" };

        var version = locked.Summary.Version;
        DraftDetail current = locked;
        for (var index = 0; index < order.Length; index++)
        {
            current = await AssignSeed().Handle(
                new AssignSeedCommand(locked.Summary.Id, order[index], seeds[index], version, _host), default);
            version = current.Summary.Version;
        }

        var seed1 = current.Participants.Where(p => p.Seed == "Seed1").Select(p => p.UserId).ToArray();
        var seed2 = current.Participants.Where(p => p.Seed == "Seed2").Select(p => p.UserId).ToArray();
        return (current, seed1, seed2);
    }
}
