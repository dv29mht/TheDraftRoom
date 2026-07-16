using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Drives the PR-14 club/protected-player round and the PR-15 position draft over real HTTP against the
/// in-memory host: a 1v1 draft runs all the way from create → seed → teams → ready → start → spinner → open
/// club selection → each team clubs+protects → open positions → every position pick → Completed, proving
/// straight club order, snake position order, club/footballer uniqueness, and completion; plus focused
/// out-of-turn / duplicate-club rejections and 2v2 either-teammate pick authority.
/// </summary>
public sealed class DraftClubAndPositionTests(DraftRoomApiFactory factory) : IClassFixture<DraftRoomApiFactory>
{
    private const string StrongPassword = "Strong@2026Pass";

    private async Task<string> AdminTokenAsync()
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return admin.AccessToken;
    }

    private async Task<(HttpClient Client, Guid UserId)> HostAsync()
    {
        var login = await factory.CreateClient().LoginAsync(SeededAccounts.PlayerEmail, SeededAccounts.PlayerPassword);
        return (factory.CreateClient().WithBearer(login.AccessToken), login.User.Id);
    }

    private async Task<(Guid UserId, HttpClient Client)> ActivePlayerAsync(string email, string name)
    {
        var admin = factory.CreateClient().WithBearer(await AdminTokenAsync());
        var create = await admin.PostAsJsonAsync("/api/users", new { email, displayName = name });
        create.EnsureSuccessStatusCode();
        var userId = (await create.Content.ReadFromJsonAsync<ManagedUser>())!.Id;

        var otp = factory.EmailSender.PasswordFor(email);
        var login = await factory.CreateClient().LoginAsync(email, otp);
        var change = await factory.CreateClient().WithBearer(login.AccessToken)
            .PostAsJsonAsync("/api/auth/change-password", new { currentPassword = otp, newPassword = StrongPassword, confirmPassword = StrongPassword });
        change.EnsureSuccessStatusCode();
        var changed = (await change.Content.ReadFromJsonAsync<LoginResponse>())!;
        return (userId, factory.CreateClient().WithBearer(changed.AccessToken));
    }

    private static async Task<LobbyDetail> OkAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LobbyDetail>())!;
    }

    private static async Task<LobbyDetail> CreateLobbyAsync(HttpClient host, string format, params Guid[] invites)
    {
        var response = await host.PostAsJsonAsync("/api/drafts", new { name = "Test Lobby", format, inviteUserIds = invites });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LobbyDetail>())!;
    }

    private static async Task<BoardDto> BoardAsync(HttpClient client, Guid draftId, Guid? clubId = null)
    {
        var path = clubId is null ? $"/api/drafts/{draftId}/board" : $"/api/drafts/{draftId}/board?clubId={clubId}";
        return (await client.GetFromJsonAsync<BoardDto>(path))!;
    }

    [Fact]
    public async Task Full_1v1_flow_runs_club_round_then_snake_position_draft_to_completion()
    {
        var (host, hostId) = await HostAsync();
        var (guestId, guest) = await ActivePlayerAsync("cp.g1@draftroom.test", "Guest One");
        var clientByUser = new Dictionary<Guid, HttpClient> { [hostId] = host, [guestId] = guest };

        var lobby = await CreateLobbyAsync(host, "1v1", guestId);
        var id = lobby.Summary.Id;
        var version = lobby.Summary.Version;

        version = (await OkAsync(guest, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/lock", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/teams", new { teams = Array.Empty<TeamInput>(), expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(guest, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/ready-check", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/start", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/spinner", new { expectedVersion = version })).Summary.Version;

        // Open the club round (host-only).
        var clubRound = await OkAsync(host, $"/api/drafts/{id}/open-clubs", new { expectedVersion = version });
        Assert.Equal("ClubSelection", clubRound.Summary.Status);
        version = clubRound.Summary.Version;

        // Results do not exist until the draft completes (PR-19).
        Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync($"/api/drafts/{id}/results")).StatusCode);

        // Each team, in straight spinner order, picks a distinct five-star club and protects a player from it.
        var detail = clubRound;
        var chosenClubs = new List<Guid>();
        for (var team = 0; team < 2; team++)
        {
            Assert.Equal("ClubSelection", detail.Turn.Phase);
            Assert.Equal("Straight", detail.Turn.Direction);
            var actor = clientByUser[detail.Turn.ActiveTeamMemberUserIds[0]];

            var clubs = (await BoardAsync(actor, id)).AvailableClubs;
            Assert.DoesNotContain(clubs, club => chosenClubs.Contains(club.Id)); // taken clubs are gone
            var club = clubs[0];
            chosenClubs.Add(club.Id);

            var held = (await BoardAsync(actor, id, club.Id)).EligibleFootballers[0];
            detail = await OkAsync(actor, $"/api/drafts/{id}/club-select", new { clubId = club.Id, footballerId = held.Id, expectedVersion = detail.Summary.Version });

            var chosenTeam = detail.Teams.First(candidate => candidate.SelectedClubId == club.Id);
            Assert.Equal(club.Name, chosenTeam.SelectedClubName);
            Assert.Contains(detail.Picks, pick => pick.TeamId == chosenTeam.Id && pick.SlotOrder == 0 && pick.FootballerId == held.Id);
        }

        Assert.Equal(2, chosenClubs.Distinct().Count());

        // Open the position draft (host-only), then run every pick to completion, recording snake order.
        detail = await OkAsync(host, $"/api/drafts/{id}/open-positions", new { expectedVersion = detail.Summary.Version });
        Assert.Equal("PositionDraft", detail.Summary.Status);

        var rankById = detail.Teams.ToDictionary(team => team.Id, team => team.SpinnerRank!.Value);
        var pickRanks = new List<int>();
        var guard = 0;
        while (detail.Summary.Status == "PositionDraft")
        {
            Assert.True(guard++ < 40, "the position draft did not complete");
            pickRanks.Add(rankById[detail.Turn.ActiveTeamId!.Value]);

            var actor = clientByUser[detail.Turn.ActiveTeamMemberUserIds[0]];
            var board = await BoardAsync(actor, id);
            Assert.True(board.IsMyTurn);
            var footballer = board.EligibleFootballers[0];
            // The pick fills the announced slot's position (or any, for a flexible sub).
            if (!detail.Turn.SlotAcceptsAnyPosition)
            {
                Assert.Contains(detail.Turn.ActiveSlotPosition!, footballer.Positions);
            }
            detail = await OkAsync(actor, $"/api/drafts/{id}/pick", new { footballerId = footballer.Id, expectedVersion = detail.Summary.Version });
        }

        // Snake order: round 1 = 1,2; round 2 = 2,1; … over 15 rounds for 2 teams.
        var expected = Enumerable.Range(0, 30)
            .Select(pick => (pick / 2 % 2 == 0) ? (pick % 2) + 1 : 2 - (pick % 2))
            .ToArray();
        Assert.Equal(expected, pickRanks);

        Assert.Equal("Completed", detail.Summary.Status);
        foreach (var team in detail.Teams)
        {
            Assert.Equal(16, detail.Picks.Count(pick => pick.TeamId == team.Id)); // 1 held + 15 drafted
        }
        Assert.Equal(detail.Picks.Count, detail.Picks.Select(pick => pick.FootballerId).Distinct().Count());

        // PR-19 (§9.7): the completed draft now serves its results — full squads with frozen ratings,
        // 4-3-3 line ratings, pinned-dataset club/league/nation extras, and the 1..32 pick sequence.
        var results = (await host.GetFromJsonAsync<DraftResults>($"/api/drafts/{id}/results"))!;
        Assert.Equal(2, results.Teams.Count);
        foreach (var team in results.Teams)
        {
            Assert.Equal(16, team.Picks.Count);
            Assert.Equal(Math.Round(team.Picks.Average(pick => pick.FootballerOverall), 1), team.AverageOverall);
            var lines = team.LineRatings.ToDictionary(line => line.Line);
            Assert.Equal(1, lines["GK"].SlotCount);
            Assert.Equal(4, lines["DEF"].SlotCount);
            Assert.Equal(3, lines["MID"].SlotCount);
            Assert.Equal(3, lines["FWD"].SlotCount);
            Assert.NotEmpty(team.Clubs);
            Assert.NotEmpty(team.Leagues);
            Assert.NotEmpty(team.Nations);
            Assert.NotNull(team.SelectedClubName);
        }
        Assert.Equal(Enumerable.Range(1, 32), results.PickSequence.Select(pick => pick.Sequence));

        // Results follow the 404-not-403 rule: a non-participant cannot read (or discover) them.
        var (_, outsider) = await ActivePlayerAsync("cp.res1@draftroom.test", "Results Outsider");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/drafts/{id}/results")).StatusCode);
    }

    [Fact]
    public async Task Out_of_turn_and_duplicate_club_choices_are_rejected()
    {
        var (host, hostId) = await HostAsync();
        var (guestId, guest) = await ActivePlayerAsync("cp.g2@draftroom.test", "Guest Two");
        var clientByUser = new Dictionary<Guid, HttpClient> { [hostId] = host, [guestId] = guest };

        var lobby = await CreateLobbyAsync(host, "1v1", guestId);
        var id = lobby.Summary.Id;
        var version = lobby.Summary.Version;
        version = (await OkAsync(guest, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/lock", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/teams", new { teams = Array.Empty<TeamInput>(), expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(guest, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/ready-check", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/start", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/spinner", new { expectedVersion = version })).Summary.Version;
        var detail = await OkAsync(host, $"/api/drafts/{id}/open-clubs", new { expectedVersion = version });

        var activeMember = detail.Turn.ActiveTeamMemberUserIds[0];
        var inactiveMember = clientByUser.Keys.First(userId => userId != activeMember);
        var board = await BoardAsync(clientByUser[activeMember], id);
        var club = board.AvailableClubs[0];
        var held = (await BoardAsync(clientByUser[activeMember], id, club.Id)).EligibleFootballers[0];

        // The team that is NOT on the clock cannot choose (out of turn).
        var outOfTurn = await clientByUser[inactiveMember].PostAsJsonAsync(
            $"/api/drafts/{id}/club-select", new { clubId = club.Id, footballerId = held.Id, expectedVersion = detail.Summary.Version });
        Assert.Equal(HttpStatusCode.BadRequest, outOfTurn.StatusCode);

        // Active team chooses the club; the next team cannot take the same club.
        detail = await OkAsync(clientByUser[activeMember], $"/api/drafts/{id}/club-select", new { clubId = club.Id, footballerId = held.Id, expectedVersion = detail.Summary.Version });
        var nextMember = detail.Turn.ActiveTeamMemberUserIds[0];
        var nextHeld = (await BoardAsync(clientByUser[nextMember], id, club.Id)).EligibleFootballers.FirstOrDefault();
        // The taken club is no longer offered; force the duplicate by id anyway and expect a rejection.
        var duplicate = await clientByUser[nextMember].PostAsJsonAsync(
            $"/api/drafts/{id}/club-select", new { clubId = club.Id, footballerId = nextHeld?.Id ?? held.Id, expectedVersion = detail.Summary.Version });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Board_search_and_footballer_cards_serve_the_room_from_the_pinned_pool()
    {
        var (host, hostId) = await HostAsync();
        var (guestId, guest) = await ActivePlayerAsync("cp.g3@draftroom.test", "Guest Three");
        var clientByUser = new Dictionary<Guid, HttpClient> { [hostId] = host, [guestId] = guest };

        var lobby = await CreateLobbyAsync(host, "1v1", guestId);
        var id = lobby.Summary.Id;
        var version = lobby.Summary.Version;
        version = (await OkAsync(guest, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/lock", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/teams", new { teams = Array.Empty<TeamInput>(), expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(guest, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/ready-check", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/start", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/spinner", new { expectedVersion = version })).Summary.Version;
        var detail = await OkAsync(host, $"/api/drafts/{id}/open-clubs", new { expectedVersion = version });
        for (var team = 0; team < 2; team++)
        {
            var actor = clientByUser[detail.Turn.ActiveTeamMemberUserIds[0]];
            var club = (await BoardAsync(actor, id)).AvailableClubs[0];
            var held = (await BoardAsync(actor, id, club.Id)).EligibleFootballers[0];
            detail = await OkAsync(actor, $"/api/drafts/{id}/club-select", new { clubId = club.Id, footballerId = held.Id, expectedVersion = detail.Summary.Version });
        }
        detail = await OkAsync(host, $"/api/drafts/{id}/open-positions", new { expectedVersion = detail.Summary.Version });

        // Search narrows the ST pool by name without leaving the pinned pool; take bounds it deliberately.
        var pool = (await BoardAsync(host, id)).EligibleFootballers;
        var fragment = pool[0].Name.Split(' ')[^1];
        var searched = (await host.GetFromJsonAsync<BoardDto>($"/api/drafts/{id}/board?search={Uri.EscapeDataString(fragment)}"))!;
        Assert.NotEmpty(searched.EligibleFootballers);
        Assert.All(searched.EligibleFootballers, footballer =>
            Assert.Contains(fragment, footballer.Name, StringComparison.OrdinalIgnoreCase));
        var bounded = (await host.GetFromJsonAsync<BoardDto>($"/api/drafts/{id}/board?take=3"))!;
        Assert.Equal(3, bounded.EligibleFootballers.Count);

        // The detail card carries the §9.6 facts; an available footballer is not taken.
        var card = (await host.GetFromJsonAsync<FootballerCardDto>($"/api/drafts/{id}/footballers/{pool[0].Id}"))!;
        Assert.False(card.IsTaken);
        Assert.Equal(pool[0].Name, card.Card.Name);
        Assert.False(string.IsNullOrEmpty(card.Card.League));
        Assert.False(string.IsNullOrEmpty(card.Card.Nation));
        Assert.NotEmpty(card.Card.Positions);

        // A held footballer's card explains who holds it and in which slot.
        var held0 = detail.Picks.First(pick => pick.SlotOrder == 0);
        var heldCard = (await host.GetFromJsonAsync<FootballerCardDto>($"/api/drafts/{id}/footballers/{held0.FootballerId}"))!;
        Assert.True(heldCard.IsTaken);
        Assert.Equal(held0.TeamId, heldCard.TakenByTeamId);
        Assert.Equal("Held player", heldCard.TakenSlotLabel);

        // Non-participants see 404 (not 403), matching every other draft read.
        var (_, outsider) = await ActivePlayerAsync("cp.g4@draftroom.test", "Guest Four");
        var hidden = await outsider.GetAsync($"/api/drafts/{id}/footballers/{pool[0].Id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [Fact]
    public async Task In_2v2_either_teammate_may_submit_a_pick()
    {
        var (host, hostId) = await HostAsync();
        var (g1, c1) = await ActivePlayerAsync("cp.t1@draftroom.test", "Mate One");
        var (g2, c2) = await ActivePlayerAsync("cp.t2@draftroom.test", "Mate Two");
        var (g3, c3) = await ActivePlayerAsync("cp.t3@draftroom.test", "Mate Three");
        var clientByUser = new Dictionary<Guid, HttpClient> { [hostId] = host, [g1] = c1, [g2] = c2, [g3] = c3 };

        var lobby = await CreateLobbyAsync(host, "2v2", g1, g2, g3);
        var id = lobby.Summary.Id;
        var version = lobby.Summary.Version;
        version = (await OkAsync(c1, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(c2, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(c3, $"/api/drafts/{id}/join", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/lock", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/seeds", new { participantUserId = hostId, seed = "Seed1", expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/seeds", new { participantUserId = g1, seed = "Seed2", expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/seeds", new { participantUserId = g2, seed = "Seed1", expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/seeds", new { participantUserId = g3, seed = "Seed2", expectedVersion = version })).Summary.Version;
        var teams = new[] { new TeamInput("Alpha", [hostId, g1]), new TeamInput("Bravo", [g2, g3]) };
        version = (await OkAsync(host, $"/api/drafts/{id}/teams", new { teams, expectedVersion = version })).Summary.Version;
        foreach (var client in new[] { host, c1, c2, c3 })
        {
            version = (await OkAsync(client, $"/api/drafts/{id}/ready", new { ready = true, expectedVersion = version })).Summary.Version;
        }
        version = (await OkAsync(host, $"/api/drafts/{id}/ready-check", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/start", new { expectedVersion = version })).Summary.Version;
        version = (await OkAsync(host, $"/api/drafts/{id}/spinner", new { expectedVersion = version })).Summary.Version;
        var detail = await OkAsync(host, $"/api/drafts/{id}/open-clubs", new { expectedVersion = version });

        // Both teams club + protect (submitted by whichever member is listed first — that is fine).
        for (var team = 0; team < 2; team++)
        {
            var actor = clientByUser[detail.Turn.ActiveTeamMemberUserIds[0]];
            var club = (await BoardAsync(actor, id)).AvailableClubs[0];
            var held = (await BoardAsync(actor, id, club.Id)).EligibleFootballers[0];
            detail = await OkAsync(actor, $"/api/drafts/{id}/club-select", new { clubId = club.Id, footballerId = held.Id, expectedVersion = detail.Summary.Version });
        }

        detail = await OkAsync(host, $"/api/drafts/{id}/open-positions", new { expectedVersion = detail.Summary.Version });

        // The active team has two members; the SECOND-listed teammate submits the first pick — either may.
        var members = detail.Turn.ActiveTeamMemberUserIds;
        Assert.Equal(2, members.Count);
        var teammate = clientByUser[members[1]];
        var pool = (await BoardAsync(teammate, id)).EligibleFootballers;
        var accepted = await OkAsync(teammate, $"/api/drafts/{id}/pick", new { footballerId = pool[0].Id, expectedVersion = detail.Summary.Version });
        Assert.Contains(accepted.Picks, pick => pick.FootballerId == pool[0].Id && pick.SlotOrder == 1);
    }
}
