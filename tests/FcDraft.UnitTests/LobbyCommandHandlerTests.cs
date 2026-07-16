using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Drafts;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the PR-11 lobby rules over the in-memory store: creation seeds host + invited participants and
/// opens the lobby; invites reject over-capacity and deactivated users; join is self-service presence;
/// remove is host-only and cannot drop the host; and locking enforces 1v1 2–10 / 2v2 4–16-even server-side.
/// </summary>
public sealed class LobbyCommandHandlerTests
{
    private readonly InMemoryDraftStore _store = new();
    private readonly InMemoryTransactionRunner _runner = new();
    private readonly FakeRosterTemplateService _templates = new();
    private readonly FakeIdentityDirectory _identity = new();
    private readonly Guid _host;

    public LobbyCommandHandlerTests() => _host = _identity.Add("Host").Id;

    private CreateDraftCommandHandler Create() => new(_store, _templates, _identity, _runner, TestNotifiers.Lifecycle(_identity));
    private InviteParticipantCommandHandler Invite() => new(_store, _identity, _runner, TestNotifiers.Lifecycle(_identity));
    private JoinDraftCommandHandler Join() => new(_store, _identity, _runner);
    private RemoveParticipantCommandHandler Remove() => new(_store, _identity, _runner);
    private LockLobbyCommandHandler Lock() => new(_store, _identity, _runner);

    private Guid[] AddPlayers(int count) =>
        Enumerable.Range(0, count).Select(_ => _identity.Add("Player").Id).ToArray();

    private Task<DraftDetail> CreateLobbyAsync(string format = "2v2", params Guid[] invites) =>
        Create().Handle(new CreateDraftCommand("Lobby", format, _host, null, invites), default);

    // --- Creation -----------------------------------------------------------------------------------

    [Fact]
    public async Task Create_seeds_the_host_and_invited_participants()
    {
        var invites = AddPlayers(2);

        var lobby = await CreateLobbyAsync("2v2", invites);

        Assert.Equal("Lobby", lobby.Summary.Status);
        Assert.Equal(3, lobby.Participants.Count);
        Assert.Equal(_host, lobby.Summary.HostUserId);
        var host = Assert.Single(lobby.Participants, p => p.IsHost);
        Assert.Equal("Joined", host.Status);
        Assert.All(lobby.Participants.Where(p => !p.IsHost), p => Assert.Equal("Invited", p.Status));
        Assert.NotNull(host.DisplayName);
    }

    [Fact]
    public async Task Create_over_capacity_is_rejected()
    {
        // 1v1 holds at most 10 including the host, so 10 invitees (11 total) is too many.
        var invites = AddPlayers(10);

        await Assert.ThrowsAsync<ValidationAppException>(() => CreateLobbyAsync("1v1", invites));
    }

    [Fact]
    public async Task Create_with_a_deactivated_invitee_is_rejected()
    {
        var deactivated = _identity.Add("Gone", AccountStatus.Deactivated).Id;

        await Assert.ThrowsAsync<ValidationAppException>(() => CreateLobbyAsync("2v2", deactivated));
    }

    [Fact]
    public async Task Create_by_a_deactivated_host_is_forbidden()
    {
        var host = _identity.Add("Benched", AccountStatus.Deactivated).Id;

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Create().Handle(new CreateDraftCommand("Lobby", "1v1", host), default));
    }

    // --- Invite -------------------------------------------------------------------------------------

    [Fact]
    public async Task Invite_adds_an_invited_participant_and_appends_an_event()
    {
        var lobby = await CreateLobbyAsync("1v1");
        var invitee = _identity.Add("Newcomer").Id;

        var updated = await Invite().Handle(
            new InviteParticipantCommand(lobby.Summary.Id, invitee, lobby.Summary.Version, _host), default);

        Assert.Equal(2, updated.Participants.Count);
        Assert.Contains(updated.Participants, p => p.UserId == invitee && p.Status == "Invited");

        var stored = await _store.FindAsync(lobby.Summary.Id, default);
        Assert.Contains(stored!.Events, evt => evt.Type == DraftEventType.ParticipantInvited);
    }

    [Fact]
    public async Task Invite_of_a_deactivated_user_is_rejected()
    {
        var lobby = await CreateLobbyAsync("1v1");
        var deactivated = _identity.Add("Gone", AccountStatus.Deactivated).Id;

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Invite().Handle(new InviteParticipantCommand(lobby.Summary.Id, deactivated, lobby.Summary.Version, _host), default));
    }

    [Fact]
    public async Task Invite_of_an_existing_participant_is_rejected()
    {
        var invite = AddPlayers(1);
        var lobby = await CreateLobbyAsync("1v1", invite);

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Invite().Handle(new InviteParticipantCommand(lobby.Summary.Id, invite[0], lobby.Summary.Version, _host), default));
    }

    [Fact]
    public async Task Invite_beyond_capacity_is_rejected()
    {
        // Fill a 1v1 lobby to its max of 10 (host + 9), then a further invite must be rejected.
        var lobby = await CreateLobbyAsync("1v1", AddPlayers(9));
        var overflow = _identity.Add("Eleven").Id;

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Invite().Handle(new InviteParticipantCommand(lobby.Summary.Id, overflow, lobby.Summary.Version, _host), default));
    }

    [Fact]
    public async Task Invite_by_a_non_host_is_forbidden()
    {
        var lobby = await CreateLobbyAsync("1v1");
        var stranger = _identity.Add("Stranger").Id;
        var invitee = _identity.Add("Newcomer").Id;

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Invite().Handle(new InviteParticipantCommand(lobby.Summary.Id, invitee, lobby.Summary.Version, stranger), default));
    }

    [Fact]
    public async Task Invite_with_a_stale_version_conflicts()
    {
        var lobby = await CreateLobbyAsync("1v1");
        var first = _identity.Add("First").Id;
        var second = _identity.Add("Second").Id;
        await Invite().Handle(new InviteParticipantCommand(lobby.Summary.Id, first, lobby.Summary.Version, _host), default);

        await Assert.ThrowsAsync<ConflictAppException>(() =>
            Invite().Handle(new InviteParticipantCommand(lobby.Summary.Id, second, lobby.Summary.Version, _host), default));
    }

    // --- Join ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_invited_participant_can_join()
    {
        var invite = AddPlayers(1);
        var lobby = await CreateLobbyAsync("1v1", invite);

        var joined = await Join().Handle(
            new JoinDraftCommand(lobby.Summary.Id, lobby.Summary.Version, invite[0]), default);

        Assert.Contains(joined.Participants, p => p.UserId == invite[0] && p.Status == "Joined");
        Assert.Equal(2, joined.Capacity.JoinedCount);
    }

    [Fact]
    public async Task Joining_is_idempotent()
    {
        var invite = AddPlayers(1);
        var lobby = await CreateLobbyAsync("1v1", invite);
        var joined = await Join().Handle(new JoinDraftCommand(lobby.Summary.Id, lobby.Summary.Version, invite[0]), default);

        // A repeat join at the now-current version is a no-op, not a conflict or a duplicate event.
        var again = await Join().Handle(new JoinDraftCommand(lobby.Summary.Id, joined.Summary.Version, invite[0]), default);
        Assert.Equal(joined.Summary.Version, again.Summary.Version);
    }

    [Fact]
    public async Task A_non_participant_cannot_join()
    {
        var lobby = await CreateLobbyAsync("1v1");
        var stranger = _identity.Add("Stranger").Id;

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Join().Handle(new JoinDraftCommand(lobby.Summary.Id, lobby.Summary.Version, stranger), default));
    }

    // --- Remove -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_host_can_remove_a_participant()
    {
        var invite = AddPlayers(1);
        var lobby = await CreateLobbyAsync("1v1", invite);

        var updated = await Remove().Handle(
            new RemoveParticipantCommand(lobby.Summary.Id, invite[0], lobby.Summary.Version, _host), default);

        Assert.DoesNotContain(updated.Participants, p => p.UserId == invite[0]);
        var stored = await _store.FindAsync(lobby.Summary.Id, default);
        Assert.Contains(stored!.Events, evt => evt.Type == DraftEventType.ParticipantRemoved);
    }

    [Fact]
    public async Task The_host_cannot_be_removed()
    {
        var lobby = await CreateLobbyAsync("1v1");

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Remove().Handle(new RemoveParticipantCommand(lobby.Summary.Id, _host, lobby.Summary.Version, _host), default));
    }

    [Fact]
    public async Task Remove_by_a_non_host_is_forbidden()
    {
        var invite = AddPlayers(1);
        var lobby = await CreateLobbyAsync("1v1", invite);
        var stranger = _identity.Add("Stranger").Id;

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Remove().Handle(new RemoveParticipantCommand(lobby.Summary.Id, invite[0], lobby.Summary.Version, stranger), default));
    }

    // --- Lock (the capacity gate) -------------------------------------------------------------------

    [Fact]
    public async Task Locking_a_1v1_below_the_minimum_is_rejected()
    {
        var lobby = await CreateLobbyAsync("1v1"); // host only = 1, below the minimum of 2

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Lock().Handle(new LockLobbyCommand(lobby.Summary.Id, lobby.Summary.Version, _host), default));
    }

    [Fact]
    public async Task Locking_a_valid_1v1_advances_to_team_formation()
    {
        var lobby = await CreateLobbyAsync("1v1", AddPlayers(1)); // host + 1 = 2

        var locked = await Lock().Handle(new LockLobbyCommand(lobby.Summary.Id, lobby.Summary.Version, _host), default);

        Assert.Equal("TeamFormation", locked.Summary.Status);
        var stored = await _store.FindAsync(lobby.Summary.Id, default);
        Assert.Contains(stored!.Events, evt => evt.Type == DraftEventType.LobbyLocked);
    }

    [Theory]
    [InlineData(2)] // host + 2 = 3, below the 2v2 minimum of 4
    [InlineData(4)] // host + 4 = 5, odd
    public async Task Locking_a_2v2_below_minimum_or_odd_is_rejected(int inviteCount)
    {
        var lobby = await CreateLobbyAsync("2v2", AddPlayers(inviteCount));

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Lock().Handle(new LockLobbyCommand(lobby.Summary.Id, lobby.Summary.Version, _host), default));
    }

    [Fact]
    public async Task Locking_a_valid_even_2v2_advances_to_team_formation()
    {
        var lobby = await CreateLobbyAsync("2v2", AddPlayers(3)); // host + 3 = 4

        var locked = await Lock().Handle(new LockLobbyCommand(lobby.Summary.Id, lobby.Summary.Version, _host), default);

        Assert.Equal("TeamFormation", locked.Summary.Status);
        Assert.False(locked.Capacity.CanLock); // no longer an open lobby
    }

    [Fact]
    public async Task Locking_by_a_non_host_is_forbidden()
    {
        var lobby = await CreateLobbyAsync("1v1", AddPlayers(1));
        var stranger = _identity.Add("Stranger").Id;

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Lock().Handle(new LockLobbyCommand(lobby.Summary.Id, lobby.Summary.Version, stranger), default));
    }
}
