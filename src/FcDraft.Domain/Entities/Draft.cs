namespace FcDraft.Domain.Entities;

/// <summary>
/// The authoritative draft aggregate (PR-10). A draft advances through the lifecycle in
/// <see cref="DraftStateMachine"/> (PRD §10.1); every accepted transition bumps <see cref="Version"/>
/// and appends one immutable <see cref="DraftEvent"/>, so the current state can always be rebuilt or
/// verified from history (see <see cref="DraftStateProjection"/>). At start the active roster template
/// and dataset version are snapshotted (<see cref="SnapshotConfiguration"/>) so later edits cannot
/// mutate an in-progress draft. Participants, teams, seeding, the spinner, and picks are layered on by
/// PR-11–PR-16; this type is the durable foundation they build on.
/// </summary>
public sealed class Draft
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Short, human-shareable code (like the legacy room code); unique per draft.</summary>
    public required string Code { get; init; }

    public required string Name { get; set; }
    public required DraftFormat Format { get; init; }

    public DraftStatus Status { get; private set; } = DraftStatus.Draft;

    /// <summary>The account that created and hosts the draft. Only the host (or an admin) may drive it.</summary>
    public required Guid HostUserId { get; init; }

    /// <summary>The roster template this draft snapshots at start; bound at creation from the active template.</summary>
    public required Guid RosterTemplateId { get; init; }

    /// <summary>
    /// Optimistic concurrency version. Starts at 1 (the <see cref="DraftEventType.DraftCreated"/> event)
    /// and increments by one for every accepted transition. Commands carry the last-seen version; a
    /// mismatch is a conflict (PRD §6.5). Mapped as an EF concurrency token so simultaneous writes lose.
    /// </summary>
    public int Version { get; private set; } = 1;

    /// <summary>Snapshotted from the active template's pick timer at start (PRD §6.4 default 120s).</summary>
    public int PickTimerSeconds { get; private set; } = 120;

    /// <summary>The dataset version pinned at start; an in-progress draft never follows later activations.</summary>
    public Guid? PinnedDatasetVersionId { get; private set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// When the active position turn's 120s clock started (PR-16, PRD §6.4). Set when the position draft
    /// opens and after every accepted pick; null outside the timed position rounds. Persisted, so a
    /// restarted server or refreshed client computes the SAME remaining time from
    /// <c>TurnStartedAt + PickTimerSeconds − now</c> instead of trusting any in-process countdown.
    /// </summary>
    public DateTimeOffset? TurnStartedAt { get; private set; }

    /// <summary>
    /// When the draft was paused (PR-16, PRD §9.6). While set, the turn clock is frozen: remaining time is
    /// measured against this instant, and resuming shifts <see cref="TurnStartedAt"/> forward by the pause
    /// duration so paused time never elapses.
    /// </summary>
    public DateTimeOffset? PausedAt { get; private set; }

    /// <summary>The active turn's absolute deadline, or null when no timed turn is running.</summary>
    public DateTimeOffset? TurnDeadline => TurnStartedAt?.AddSeconds(PickTimerSeconds);

    /// <summary>True when a timed, unpaused position turn has run past its deadline (auto-pick is due).</summary>
    public bool HasExpiredTurn(DateTimeOffset now) =>
        Status == DraftStatus.PositionDraft && PausedAt is null && TurnDeadline is { } deadline && now >= deadline;

    public ICollection<DraftParticipant> Participants { get; init; } = new List<DraftParticipant>();
    public ICollection<DraftTeam> Teams { get; init; } = new List<DraftTeam>();
    public ICollection<DraftRosterSlot> Slots { get; init; } = new List<DraftRosterSlot>();

    /// <summary>Every held (Order 0) and drafted (Order 1..N) footballer selection (PR-14/PR-15).</summary>
    public ICollection<DraftPick> Picks { get; init; } = new List<DraftPick>();

    public ICollection<DraftEvent> Events { get; init; } = new List<DraftEvent>();

    /// <summary>
    /// Creates a draft in the <see cref="DraftStatus.Draft"/> state and records the opening
    /// <see cref="DraftEventType.DraftCreated"/> event (sequence 1, version 1). Constructing every draft
    /// through here keeps the "history always begins with DraftCreated" invariant in one place.
    /// </summary>
    public static Draft Create(string name, DraftFormat format, Guid hostUserId, Guid rosterTemplateId, string code)
    {
        var draft = new Draft
        {
            Code = code,
            Name = name,
            Format = format,
            HostUserId = hostUserId,
            RosterTemplateId = rosterTemplateId,
        };
        draft.Append(DraftEventType.DraftCreated, fromStatus: null, toStatus: DraftStatus.Draft, actorUserId: hostUserId, reason: null, payload: null);
        return draft;
    }

    /// <summary>
    /// Applies an allowed lifecycle transition (§10.1), bumps <see cref="Version"/>, stamps the
    /// start/complete timestamps, and appends one immutable <see cref="DraftEvent"/>. Throws
    /// <see cref="InvalidDraftTransitionException"/> for a disallowed move — command handlers pre-check
    /// with <see cref="DraftStateMachine.IsAllowed"/> so callers see a validation error, not a 500;
    /// this guard is the aggregate's own defence in depth.
    /// </summary>
    public DraftEvent Transition(
        DraftStatus target,
        DraftEventType eventType,
        Guid? actorUserId,
        string? reason = null,
        string? payload = null)
    {
        if (!DraftStateMachine.IsAllowed(Status, target))
        {
            throw new InvalidDraftTransitionException(Status, target);
        }

        var from = Status;
        Status = target;
        Version++;

        if (target == DraftStatus.SpinnerRanking && StartedAt is null)
        {
            StartedAt = DateTimeOffset.UtcNow;
        }
        if (target == DraftStatus.Completed)
        {
            CompletedAt = DateTimeOffset.UtcNow;
        }

        return Append(eventType, from, target, actorUserId, reason, payload);
    }

    /// <summary>
    /// Freezes the draft's configuration at start (PRD §9.4): copies the active template's ordered slots
    /// into <see cref="Slots"/>, snapshots the pick timer, and pins the dataset version. Once-only — a
    /// second call throws, so a retried start cannot double-write the snapshot.
    /// </summary>
    public void SnapshotConfiguration(IEnumerable<DraftRosterSlot> slots, int pickTimerSeconds, Guid? datasetVersionId)
    {
        if (Slots.Count > 0)
        {
            throw new InvalidOperationException("The draft configuration has already been snapshotted.");
        }

        PickTimerSeconds = pickTimerSeconds;
        PinnedDatasetVersionId = datasetVersionId;
        foreach (var slot in slots.OrderBy(slot => slot.Order))
        {
            Slots.Add(slot);
        }
    }

    // --- Lobby lifecycle (PR-11) --------------------------------------------------------------------
    // Each accepted mutation below bumps Version and appends exactly one DraftEvent, so the lobby's
    // attendance is reconstructable from history like every other draft change. Capacity/authorization
    // validation lives in the command handlers (PRD §6.2, §9.4); these methods carry light defensive
    // guards and own the state mutation + event append.

    /// <summary>
    /// Adds the creating host as a joined participant and opens the lobby (Draft → Lobby, PRD §9.4).
    /// The host joining is what turns a freshly-created draft into an open lobby, so this records a
    /// <see cref="DraftEventType.ParticipantJoined"/> for them. Callable once, only from the Draft state.
    /// </summary>
    public DraftEvent OpenLobby(Guid hostUserId)
    {
        if (Status != DraftStatus.Draft)
        {
            throw new InvalidOperationException("A lobby can only be opened from the Draft state.");
        }

        Participants.Add(new DraftParticipant
        {
            DraftId = Id,
            UserId = hostUserId,
            IsHost = true,
            Status = DraftParticipantStatus.Joined,
        });
        return Transition(DraftStatus.Lobby, DraftEventType.ParticipantJoined, hostUserId, payload: hostUserId.ToString());
    }

    /// <summary>Invites a user into the open lobby, appending <see cref="DraftEventType.ParticipantInvited"/>.</summary>
    public DraftEvent InviteParticipant(Guid userId, Guid actorUserId)
    {
        Version++;
        Participants.Add(new DraftParticipant
        {
            DraftId = Id,
            UserId = userId,
            IsHost = false,
            Status = DraftParticipantStatus.Invited,
        });
        return Append(DraftEventType.ParticipantInvited, Status, Status, actorUserId, reason: null, payload: userId.ToString());
    }

    /// <summary>Marks an invited participant present, appending <see cref="DraftEventType.ParticipantJoined"/>.</summary>
    public DraftEvent MarkParticipantJoined(Guid userId)
    {
        var participant = Participants.FirstOrDefault(candidate => candidate.UserId == userId)
            ?? throw new InvalidOperationException("The user is not a participant of this draft.");
        participant.Status = DraftParticipantStatus.Joined;
        Version++;
        return Append(DraftEventType.ParticipantJoined, Status, Status, userId, reason: null, payload: userId.ToString());
    }

    /// <summary>Removes a participant before start, appending <see cref="DraftEventType.ParticipantRemoved"/>.</summary>
    public DraftEvent RemoveParticipant(Guid userId, Guid actorUserId)
    {
        var participant = Participants.FirstOrDefault(candidate => candidate.UserId == userId)
            ?? throw new InvalidOperationException("The user is not a participant of this draft.");
        Participants.Remove(participant);
        Version++;
        return Append(DraftEventType.ParticipantRemoved, Status, Status, actorUserId, reason: null, payload: userId.ToString());
    }

    /// <summary>
    /// Locks the confirmed lobby and enters team formation (Lobby → TeamFormation, PRD §10.1), appending
    /// <see cref="DraftEventType.LobbyLocked"/>. The capacity rules (§6.2) are validated by the handler
    /// before this is called; what happens inside team formation (seeding, teams) is PR-12.
    /// </summary>
    public DraftEvent LockLobby(Guid actorUserId) =>
        Transition(DraftStatus.TeamFormation, DraftEventType.LobbyLocked, actorUserId);

    // --- Team formation, seeding, and ready check (PR-12) -------------------------------------------
    // Each mutation bumps Version and appends exactly one DraftEvent (spinner commit appends two, see
    // below). The validity rules — 2v2 seed pairing, full assignment, readiness — live in the command
    // handlers (PRD §6.2, §9.4); these methods carry light defensive guards and own the state + event.

    /// <summary>
    /// Assigns (or clears) a participant's 2v2 lobby seed and appends
    /// <see cref="DraftEventType.ParticipantSeedAssigned"/>. Whether the format allows seeds and whether the
    /// draft is still in team formation is validated by the handler (PRD §6.2).
    /// </summary>
    public DraftEvent AssignSeed(Guid userId, DraftSeed? seed, Guid actorUserId)
    {
        var participant = Participants.FirstOrDefault(candidate => candidate.UserId == userId)
            ?? throw new InvalidOperationException("The user is not a participant of this draft.");
        participant.Seed = seed;
        Version++;
        return Append(DraftEventType.ParticipantSeedAssigned, Status, Status, actorUserId, reason: null, payload: $"{userId}:{seed}");
    }

    /// <summary>
    /// Replaces the draft's teams with the supplied layout (solo teams for 1v1, paired teams for 2v2) and
    /// appends <see cref="DraftEventType.TeamsFormed"/>. Re-forming clears every participant's readiness, so
    /// a changed layout must be re-confirmed before start. The team/seed validity rules are enforced by the
    /// handler; member ids are <see cref="DraftParticipant.Id"/> values the handler has already resolved.
    /// </summary>
    public DraftEvent FormTeams(IReadOnlyList<FormedTeam> teams, Guid actorUserId)
    {
        Teams.Clear();
        foreach (var participant in Participants)
        {
            participant.IsReady = false;
        }

        foreach (var spec in teams)
        {
            var team = new DraftTeam { DraftId = Id, Name = spec.Name };
            foreach (var participantId in spec.MemberParticipantIds)
            {
                team.Members.Add(new DraftTeamMember { DraftId = Id, DraftTeamId = team.Id, ParticipantId = participantId });
            }

            Teams.Add(team);
        }

        Version++;
        return Append(DraftEventType.TeamsFormed, Status, Status, actorUserId, reason: null, payload: $"{teams.Count} teams");
    }

    /// <summary>Sets a participant's readiness (self-service) and appends <see cref="DraftEventType.ParticipantReadied"/>.</summary>
    public DraftEvent SetReady(Guid userId, bool ready)
    {
        var participant = Participants.FirstOrDefault(candidate => candidate.UserId == userId)
            ?? throw new InvalidOperationException("The user is not a participant of this draft.");
        participant.IsReady = ready;
        Version++;
        return Append(DraftEventType.ParticipantReadied, Status, Status, userId, reason: null, payload: $"{userId}:{ready}");
    }

    /// <summary>
    /// Opens the ready check (TeamFormation → ReadyCheck, PRD §10.1), appending
    /// <see cref="DraftEventType.ReadyCheckStarted"/>. Attendance/assignment/team validity are checked by the
    /// handler before this is called.
    /// </summary>
    public DraftEvent BeginReadyCheck(Guid actorUserId) =>
        Transition(DraftStatus.ReadyCheck, DraftEventType.ReadyCheckStarted, actorUserId);

    /// <summary>
    /// Reopens team formation to fix teams (ReadyCheck → TeamFormation, PRD §10.1), clearing readiness so the
    /// revised teams are re-confirmed, and appends <see cref="DraftEventType.TeamFormationReopened"/>.
    /// </summary>
    public DraftEvent ReopenTeamFormation(Guid actorUserId)
    {
        foreach (var participant in Participants)
        {
            participant.IsReady = false;
        }

        return Transition(DraftStatus.TeamFormation, DraftEventType.TeamFormationReopened, actorUserId);
    }

    /// <summary>
    /// Records the server-authoritative spinner order (PR-13): assigns each team its unique
    /// <see cref="DraftTeam.SpinnerRank"/> in the given order and appends
    /// <see cref="DraftEventType.SpinnerOrderCommitted"/> then <see cref="DraftEventType.SpinnerOrderRevealed"/>
    /// at the same version. The order is decided by the handler through an injected shuffle so the visual wheel
    /// can never influence it. Callers guard against a second commit, so a retry cannot reshuffle a committed result.
    /// </summary>
    public void CommitSpinnerOrder(IReadOnlyList<Guid> orderedTeamIds, Guid actorUserId)
    {
        for (var index = 0; index < orderedTeamIds.Count; index++)
        {
            var team = Teams.FirstOrDefault(candidate => candidate.Id == orderedTeamIds[index])
                ?? throw new InvalidOperationException("A committed team id does not belong to this draft.");
            team.SpinnerRank = index + 1;
        }

        Version++;
        Append(DraftEventType.SpinnerOrderCommitted, Status, Status, actorUserId, reason: null, payload: string.Join(",", orderedTeamIds));
        Append(DraftEventType.SpinnerOrderRevealed, Status, Status, actorUserId, reason: null, payload: null);
    }

    // --- Five-star club + protected-player round and the position draft (PR-14 / PR-15) --------------
    // The pre-draft club/held round runs in straight spinner order; the position rounds snake. Turn,
    // eligibility, uniqueness, and authority are validated by the command handlers (DRAFT_RULES §2/§5);
    // these methods carry light defensive guards and own the state mutation + event append(s). Each
    // accepted change bumps Version and appends one event (the combined club+held choice appends two).

    /// <summary>
    /// Opens the pre-draft club/held round (SpinnerRanking → ClubSelection, PRD §9.5), appending
    /// <see cref="DraftEventType.ClubSelectionStarted"/>. The spinner order must be committed first; the
    /// handler checks that before calling.
    /// </summary>
    public DraftEvent OpenClubSelection(Guid actorUserId) =>
        Transition(DraftStatus.ClubSelection, DraftEventType.ClubSelectionStarted, actorUserId);

    /// <summary>
    /// Records a team's combined five-star club + protected-player choice (PR-14): sets the team's
    /// <see cref="DraftTeam.SelectedClubId"/>, adds the held <see cref="DraftPick"/> (slot Order 0), and
    /// appends <see cref="DraftEventType.ClubSelected"/> then <see cref="DraftEventType.FootballerProtected"/>
    /// at the same version — one turn, one version bump, like the spinner commit. Club uniqueness, footballer
    /// eligibility (75+, from the chosen club), and global availability are validated by the handler and,
    /// transactionally, by the unique indexes.
    /// </summary>
    public void SelectClubAndProtect(
        Guid teamId, Guid clubId, DraftPickFootballer footballer, Guid? pickedByParticipantId, Guid actorUserId)
    {
        var team = Teams.FirstOrDefault(candidate => candidate.Id == teamId)
            ?? throw new InvalidOperationException("The team does not belong to this draft.");

        team.SelectedClubId = clubId;
        Picks.Add(NewPick(teamId, HeldSlotOrder, footballer, pickedByParticipantId));

        Version++;
        Append(DraftEventType.ClubSelected, Status, Status, actorUserId, reason: null, payload: $"{teamId}:{clubId}");
        Append(DraftEventType.FootballerProtected, Status, Status, actorUserId, reason: null, payload: $"{teamId}:{footballer.FootballerId}");
    }

    /// <summary>
    /// Opens the position draft (ClubSelection → PositionDraft, PRD §9.6), appending
    /// <see cref="DraftEventType.PositionRoundStarted"/>. The handler checks every team has locked its club
    /// and protected player before calling.
    /// </summary>
    public DraftEvent OpenPositionDraft(Guid actorUserId) =>
        Transition(DraftStatus.PositionDraft, DraftEventType.PositionRoundStarted, actorUserId);

    /// <summary>
    /// Records an accepted position pick (PR-15): adds the drafted <see cref="DraftPick"/> for the given
    /// slot and appends <see cref="DraftEventType.PickAccepted"/> — or, when the 120s clock expired and the
    /// server picked for the team (PR-16, DRAFT_RULES decision 7), <see cref="DraftEventType.PickAutoSelected"/>
    /// with a null actor. Turn, position eligibility, rating, availability, and 2v2 first-valid-wins authority
    /// are enforced by the handler; the unique indexes make slot-fill and footballer uniqueness transactional.
    /// The completion transition is a separate step (<see cref="CompleteDraft"/>) the handler runs when the
    /// final slot is filled.
    /// </summary>
    public DraftEvent AcceptPick(
        Guid teamId, int slotOrder, DraftPickFootballer footballer, Guid? pickedByParticipantId, Guid? actorUserId,
        bool isAutoPick = false)
    {
        if (slotOrder <= HeldSlotOrder)
        {
            throw new InvalidOperationException("A position pick fills a slot after the held slot (Order 1+).");
        }

        Picks.Add(NewPick(teamId, slotOrder, footballer, pickedByParticipantId));
        Version++;
        return Append(
            isAutoPick ? DraftEventType.PickAutoSelected : DraftEventType.PickAccepted,
            Status,
            Status,
            actorUserId,
            reason: isAutoPick ? "Pick timer expired; the server drafted the best available eligible footballer." : null,
            payload: $"{teamId}:{slotOrder}:{footballer.FootballerId}");
    }

    // --- Server timer and host controls (PR-16) -------------------------------------------------------
    // The turn clock is persisted state (TurnStartedAt/PausedAt), never an in-process countdown, so a
    // restart or refresh recomputes the same remaining time (PRD §6.4). The clock methods do NOT bump
    // Version: an anchor always changes alongside a version-bumping mutation (open/pick/pause/resume) in
    // the same transaction. Expiry evaluation and the deterministic auto-pick live in the Application layer.

    /// <summary>(Re)starts the active turn's clock at <paramref name="anchor"/>. Called when the position
    /// draft opens and after every accepted pick; for a cascaded expiry catch-up the anchor is the previous
    /// deadline (not "now") so each missed turn consumed exactly its 120 seconds.</summary>
    public void StartTurnClock(DateTimeOffset anchor) => TurnStartedAt = anchor;

    /// <summary>Stops the turn clock (the draft completed or ended); both anchors clear.</summary>
    public void ClearTurnClock()
    {
        TurnStartedAt = null;
        PausedAt = null;
    }

    /// <summary>
    /// Pauses a live draft (ClubSelection/PositionDraft → Paused, PRD §10.1) with the host's required
    /// reason, appending <see cref="DraftEventType.DraftPaused"/>. Freezes the turn clock by stamping
    /// <see cref="PausedAt"/>; remaining time is measured against that instant until resume.
    /// </summary>
    public DraftEvent PauseDraft(Guid actorUserId, string reason, DateTimeOffset now)
    {
        var paused = Transition(DraftStatus.Paused, DraftEventType.DraftPaused, actorUserId, reason);
        PausedAt = now;
        return paused;
    }

    /// <summary>
    /// Resumes a paused draft back to <paramref name="target"/> (the state it paused from), appending
    /// <see cref="DraftEventType.DraftResumed"/>. Shifts <see cref="TurnStartedAt"/> forward by the pause
    /// duration so paused time never elapsed from the turn's 120 seconds.
    /// </summary>
    public DraftEvent ResumeDraft(DraftStatus target, Guid actorUserId, string? reason, DateTimeOffset now)
    {
        if (TurnStartedAt is { } anchor && PausedAt is { } pausedAt)
        {
            TurnStartedAt = anchor + (now - pausedAt);
        }
        PausedAt = null;
        return Transition(target, DraftEventType.DraftResumed, actorUserId, reason);
    }

    /// <summary>
    /// Cancels the draft (→ Cancelled, PRD §10.1) with the required reason, appending
    /// <see cref="DraftEventType.DraftCancelled"/>. History is append-only: every prior event stays intact.
    /// </summary>
    public DraftEvent CancelDraft(Guid actorUserId, string reason)
    {
        var cancelled = Transition(DraftStatus.Cancelled, DraftEventType.DraftCancelled, actorUserId, reason);
        ClearTurnClock();
        return cancelled;
    }

    /// <summary>
    /// Applies an audited admin recovery action (PRD §9.7/§9.10): appends a compensating
    /// <see cref="DraftEventType.AdminRecoveryApplied"/> event with the required reason — the original
    /// history is never edited or deleted — and optionally restores the active turn's clock to a full
    /// timer (<paramref name="restartClockAt"/>) so a stuck draft can continue fairly.
    /// </summary>
    public DraftEvent ApplyAdminRecovery(Guid actorUserId, string reason, DateTimeOffset? restartClockAt)
    {
        if (restartClockAt is { } anchor && TurnStartedAt is not null)
        {
            TurnStartedAt = anchor;
            if (PausedAt is not null)
            {
                PausedAt = anchor; // still paused: the restored clock stays frozen at the full timer
            }
        }

        Version++;
        return Append(DraftEventType.AdminRecoveryApplied, Status, Status, actorUserId, reason, payload: null);
    }

    /// <summary>
    /// Completes the draft once the final slot is filled (PositionDraft → Completed, PRD §9.6), appending
    /// <see cref="DraftEventType.DraftCompleted"/> and stopping the turn clock. The handler calls this only
    /// after the last accepted pick.
    /// </summary>
    public DraftEvent CompleteDraft(Guid? actorUserId)
    {
        var completed = Transition(DraftStatus.Completed, DraftEventType.DraftCompleted, actorUserId);
        ClearTurnClock();
        return completed;
    }

    /// <summary>The reserved slot Order for the pre-draft held/protected pick (the 12th squad member).</summary>
    public const int HeldSlotOrder = 0;

    private DraftPick NewPick(Guid teamId, int slotOrder, DraftPickFootballer footballer, Guid? pickedByParticipantId) => new()
    {
        DraftId = Id,
        DraftTeamId = teamId,
        SlotOrder = slotOrder,
        FootballerId = footballer.FootballerId,
        FootballerName = footballer.Name,
        FootballerOverall = footballer.Overall,
        FootballerPosition = footballer.Position,
        PickedByParticipantId = pickedByParticipantId,
    };

    private DraftEvent Append(
        DraftEventType eventType,
        DraftStatus? fromStatus,
        DraftStatus? toStatus,
        Guid? actorUserId,
        string? reason,
        string? payload)
    {
        var next = Events.Count == 0 ? 1 : Events.Max(evt => evt.Sequence) + 1;
        var draftEvent = new DraftEvent
        {
            DraftId = Id,
            Sequence = next,
            Type = eventType,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Version = Version,
            ActorUserId = actorUserId,
            Reason = reason,
            Payload = payload,
        };
        Events.Add(draftEvent);
        return draftEvent;
    }
}

/// <summary>A user's membership in a draft: lobby seed, host flag, and join/ready state (populated in PR-11/12).</summary>
public sealed class DraftParticipant
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid DraftId { get; init; }
    public required Guid UserId { get; init; }
    public bool IsHost { get; init; }

    /// <summary>Lobby-scoped Seed 1/Seed 2 for 2v2 (assigned by the host in PR-12); null otherwise.</summary>
    public DraftSeed? Seed { get; set; }

    public DraftParticipantStatus Status { get; set; } = DraftParticipantStatus.Invited;
    public bool IsReady { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A solo (1v1) or paired (2v2) draft team with its committed spinner rank and chosen club.</summary>
public sealed class DraftTeam
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid DraftId { get; init; }
    public required string Name { get; set; }

    /// <summary>Unique committed spinner order within the draft (PR-13); null before the spinner runs.</summary>
    public int? SpinnerRank { get; set; }

    /// <summary>The team's chosen five-star club (PR-14); null before club selection.</summary>
    public Guid? SelectedClubId { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public ICollection<DraftTeamMember> Members { get; init; } = new List<DraftTeamMember>();
}

/// <summary>Assigns a participant to a draft team (one member for 1v1, two for 2v2).</summary>
public sealed class DraftTeamMember
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid DraftId { get; init; }
    public required Guid DraftTeamId { get; init; }

    /// <summary>The <see cref="DraftParticipant.Id"/> assigned to this team (not the user id).</summary>
    public required Guid ParticipantId { get; init; }
}

/// <summary>
/// A team the host asks the aggregate to form (PR-12): its display name and the participant ids
/// (<see cref="DraftParticipant.Id"/>) that make it up. Passed to <see cref="Draft.FormTeams"/> once the
/// handler has validated seed pairing and membership.
/// </summary>
public sealed record FormedTeam(string Name, IReadOnlyList<Guid> MemberParticipantIds);

/// <summary>
/// A slot in the draft's frozen roster snapshot — a copy of the active <see cref="RosterTemplate"/>'s
/// ordered slots taken at start. Per-team pick linkage (via DraftPick) arrives in PR-15.
/// </summary>
public sealed class DraftRosterSlot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid DraftId { get; init; }

    /// <summary>Fill order. 0 = held (pre-draft round); 1..N = starters then flexible subs.</summary>
    public required int Order { get; init; }
    public required RosterSlotType SlotType { get; init; }

    /// <summary>Required position for a starting slot; null for held/flexible slots.</summary>
    public string? Position { get; init; }
    public required string Label { get; init; }
}

/// <summary>
/// One footballer selection in a draft (PR-14/PR-15): the pre-draft held/protected pick (<see cref="SlotOrder"/>
/// 0) and every drafted position pick (Order 1..N against the frozen <see cref="DraftRosterSlot"/> snapshot).
/// A unique <c>(DraftId, FootballerId)</c> index enforces global footballer uniqueness (held or drafted → gone
/// from every pool) and a unique <c>(DraftTeamId, SlotOrder)</c> index fills each slot exactly once — so a
/// duplicate, out-of-turn race loses transactionally. <see cref="FootballerId"/> is the dataset-stable EA id
/// (matches <see cref="Footballer.ExternalId"/>); the name/overall/position are denormalized so a completed
/// squad renders without re-reading the pinned dataset.
/// </summary>
public sealed class DraftPick
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid DraftId { get; init; }
    public required Guid DraftTeamId { get; init; }

    /// <summary>The frozen slot this pick fills: 0 = held/protected, 1..N = starters then flexible subs.</summary>
    public required int SlotOrder { get; init; }

    /// <summary>The footballer's dataset-stable external id (EA id); unique per draft across held + drafted picks.</summary>
    public required int FootballerId { get; init; }

    public required string FootballerName { get; init; }
    public required int FootballerOverall { get; init; }

    /// <summary>The footballer's primary position, denormalized for display; null if unknown.</summary>
    public string? FootballerPosition { get; init; }

    /// <summary>Which teammate submitted this pick (2v2); the <see cref="DraftParticipant.Id"/>, or null for an admin/system action.</summary>
    public Guid? PickedByParticipantId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// The footballer facts a pick records, resolved from the pinned dataset by the command handler and passed to
/// the aggregate so the Domain stays free of any dataset read seam. <see cref="FootballerId"/> is the EA
/// external id; <see cref="Position"/> is the primary position (for display).
/// </summary>
public sealed record DraftPickFootballer(int FootballerId, string Name, int Overall, string? Position);

/// <summary>
/// An append-only record of one accepted lifecycle transition (PRD §10.2, §9.10). Immutable: it is the
/// draft's audit trail and the source history the current state is rebuilt/verified from. Never edited
/// or deleted — admin corrections append compensating events (PR-21).
/// </summary>
public sealed class DraftEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid DraftId { get; init; }

    /// <summary>1-based monotonic order within the draft; unique per draft. Sequence 1 is DraftCreated.</summary>
    public required int Sequence { get; init; }

    public required DraftEventType Type { get; init; }

    /// <summary>State before the transition; null for the creation event.</summary>
    public DraftStatus? FromStatus { get; init; }

    /// <summary>State after the transition.</summary>
    public DraftStatus? ToStatus { get; init; }

    /// <summary>The draft version this event produced (matches <see cref="Draft.Version"/> after it applied).</summary>
    public required int Version { get; init; }

    public Guid? ActorUserId { get; init; }

    /// <summary>Optional non-sensitive reason (e.g. a host's cancel/pause reason).</summary>
    public string? Reason { get; init; }

    /// <summary>Optional serialized detail for events that carry structured data (later PRs).</summary>
    public string? Payload { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum DraftFormat
{
    OneVsOne = 1,
    TwoVsTwo = 2,
}

public enum DraftStatus
{
    Draft = 1,
    Lobby = 2,
    TeamFormation = 3,
    ReadyCheck = 4,
    SpinnerRanking = 5,
    ClubSelection = 6,
    PositionDraft = 7,
    Paused = 8,
    Completed = 9,
    Cancelled = 10,
    Abandoned = 11,
}

/// <summary>
/// The core draft events (PRD §10.2). A handful of additions beyond the PRD list record §10.1
/// transitions/actions §10.2 did not name explicitly: <see cref="DraftAbandoned"/> (PR-10, the
/// <c>→ Abandoned</c> terminal transition); <see cref="ParticipantRemoved"/> + <see cref="LobbyLocked"/>
/// (PR-11, a host removing a lobby participant and locking a confirmed lobby into team formation);
/// <see cref="ReadyCheckStarted"/> + <see cref="TeamFormationReopened"/> (PR-12, the host moving team
/// formation into the ready check and reopening it to fix teams); <see cref="ClubSelectionStarted"/>
/// (PR-14, the SpinnerRanking → ClubSelection transition §10.2 did not name); and
/// <see cref="PickAutoSelected"/> (PR-16, the §6.4 timer-expiry auto-pick event §10.2 did not name — a
/// system action with a null actor, distinct from a human <see cref="PickAccepted"/>). All are stored as
/// strings, so adding them needs no migration.
/// </summary>
public enum DraftEventType
{
    DraftCreated = 1,
    ParticipantInvited = 2,
    ParticipantJoined = 3,
    ParticipantSeedAssigned = 4,
    TeamsFormed = 5,
    ParticipantReadied = 6,
    DraftStarted = 7,
    SpinnerOrderCommitted = 8,
    SpinnerOrderRevealed = 9,
    ClubSelected = 10,
    FootballerProtected = 11,
    PositionRoundStarted = 12,
    PickAccepted = 13,
    DraftPaused = 14,
    DraftResumed = 15,
    DraftCompleted = 16,
    DraftCancelled = 17,
    DraftAbandoned = 18,
    AdminRecoveryApplied = 19,
    ParticipantRemoved = 20,
    LobbyLocked = 21,
    ReadyCheckStarted = 22,
    TeamFormationReopened = 23,
    ClubSelectionStarted = 24,
    PickAutoSelected = 25,
}

public enum DraftParticipantStatus
{
    Invited = 1,
    Joined = 2,
}

public enum DraftSeed
{
    Seed1 = 1,
    Seed2 = 2,
}
