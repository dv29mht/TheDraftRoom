using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Drafts;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// Proves the PR-16 done-whens over the in-memory store with a settable <see cref="TestClock"/>: the 120s
/// clock anchors on persisted state (so any evaluator computes the same remaining time), the 15s warning,
/// expiry auto-picking the deterministic §6.4 best exactly once (and cascading one pick per missed turn),
/// pause freezing / resume shifting the anchor so paused time never elapses, host-only pause/cancel with a
/// required reason, and admin-only compensating recovery that never rewrites history.
/// </summary>
public sealed class DraftTimerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 07, 16, 12, 00, 00, TimeSpan.Zero);

    private readonly InMemoryDraftStore _store = new();
    private readonly InMemoryTransactionRunner _runner = new();
    private readonly FakeRosterTemplateService _templates = new();
    private readonly FakeDatasetAdminService _datasets = new();
    private readonly FakeIdentityDirectory _identity = new();
    private readonly ReversingShuffler _shuffler = new();
    private readonly FakeDraftCatalog _catalog = new();
    private readonly TestClock _clock = new(T0);
    private readonly IReadOnlyList<CatalogClub> _clubs;
    private readonly Guid _host;
    private Guid _guest;

    public DraftTimerTests()
    {
        _host = _identity.Add("Host").Id;
        _clubs = _catalog.SeedStandardLeague();
    }

    private DraftExpiryService Expiry() => new(_store, _catalog, _identity, _runner, new NullDraftNotifier(), _clock);

    private SubmitPickCommandHandler Pick() => new(_store, _identity, _catalog, _runner, Expiry(), _clock);

    private PauseDraftCommandHandler Pause() => new(_store, _identity, _catalog, _runner, Expiry(), _clock);

    private ResumeDraftCommandHandler Resume() => new(_store, _identity, _catalog, _runner, _clock);

    private CancelDraftCommandHandler Cancel() => new(_store, _identity, _catalog, _runner, _clock);

    private ApplyAdminRecoveryCommandHandler Recover() => new(_store, _identity, _catalog, _runner, _clock);

    /// <summary>Drives a 1v1 draft to open club selection (untimed) at <see cref="T0"/>.</summary>
    private async Task<DraftDetail> ClubRoundAsync()
    {
        _guest = _identity.Add("Guest").Id;
        var create = new CreateDraftCommandHandler(_store, _templates, _identity, _runner);
        var join = new JoinDraftCommandHandler(_store, _identity, _runner);
        var @lock = new LockLobbyCommandHandler(_store, _identity, _runner);
        var formTeams = new FormTeamsCommandHandler(_store, _identity, _runner);
        var setReady = new SetReadyCommandHandler(_store, _identity, _runner);
        var beginReady = new BeginReadyCheckCommandHandler(_store, _identity, _runner);
        var start = new StartDraftCommandHandler(_store, _templates, _datasets, _runner);
        var spinner = new CommitSpinnerCommandHandler(_store, _identity, _shuffler, _runner);
        var openClubs = new OpenClubSelectionCommandHandler(_store, _identity, _catalog, _runner);

        var created = await create.Handle(new CreateDraftCommand("Draft", "1v1", _host, null, [_guest]), default);
        var id = created.Summary.Id;
        var joined = await join.Handle(new JoinDraftCommand(id, created.Summary.Version, _guest), default);
        var locked = await @lock.Handle(new LockLobbyCommand(id, joined.Summary.Version, _host), default);
        var teams = await formTeams.Handle(new FormTeamsCommand(id, null, locked.Summary.Version, _host), default);
        var hostReady = await setReady.Handle(new SetReadyCommand(id, true, teams.Summary.Version, _host), default);
        var guestReady = await setReady.Handle(new SetReadyCommand(id, true, hostReady.Summary.Version, _guest), default);
        var readyCheck = await beginReady.Handle(new BeginReadyCheckCommand(id, guestReady.Summary.Version, _host), default);
        var started = await start.Handle(new StartDraftCommand(id, readyCheck.Summary.Version, _host), default);
        var spun = await spinner.Handle(new CommitSpinnerCommand(id, started.Version, _host), default);
        return await openClubs.Handle(new OpenClubSelectionCommand(id, spun.Summary.Version, _host), default);
    }

    /// <summary>Drives a 1v1 draft into the position draft; the first turn's clock anchors at <see cref="T0"/>.</summary>
    private async Task<DraftDetail> PositionDraftAsync()
    {
        var opened = await ClubRoundAsync();
        var id = opened.Summary.Id;
        var select = new SelectClubAndProtectCommandHandler(_store, _identity, _catalog, _runner);
        var openPositions = new OpenPositionDraftCommandHandler(_store, _identity, _catalog, _runner, _clock);

        var version = opened.Summary.Version;
        foreach (var rank in new[] { 1, 2 })
        {
            var detail = await SnapshotAsync(id);
            var team = detail.Teams.First(candidate => candidate.SpinnerRank == rank);
            var club = _clubs[rank - 1];
            var footballer = (await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(ClubId: club.Id), default))
                .First(candidate => detail.Picks.All(pick => pick.FootballerId != candidate.Id));
            var result = await select.Handle(new SelectClubAndProtectCommand(id, club.Id, footballer.Id, version, team.MemberUserIds[0]), default);
            version = result.Summary.Version;
        }

        return await openPositions.Handle(new OpenPositionDraftCommand(id, version, _host), default);
    }

    private async Task<Draft> DraftAsync(Guid draftId) => (await _store.FindAsync(draftId, default))!;

    private async Task<DraftDetail> SnapshotAsync(Guid draftId) =>
        DraftMapper.ToDetail(await DraftAsync(draftId), now: _clock.GetUtcNow());

    /// <summary>The §6.4 deterministic expectation: the highest overall → name → id entry still available.</summary>
    private async Task<CatalogFootballer> BestAvailableAsync(Draft draft, string? position)
    {
        var taken = draft.Picks.Select(pick => pick.FootballerId).ToHashSet();
        var pool = await _catalog.ListFootballersAsync(null, new CatalogFootballerFilter(Position: position, Take: 500), default);
        return pool.First(footballer => !taken.Contains(footballer.Id));
    }

    // --- Remaining-time math (pure, restart-safe) ----------------------------------------------------

    [Fact]
    public async Task Opening_the_position_draft_starts_a_full_clock()
    {
        var detail = await PositionDraftAsync();

        Assert.True(detail.Timer.IsTimed);
        Assert.False(detail.Timer.IsPaused);
        Assert.Equal(120, detail.Timer.PickTimerSeconds);
        Assert.Equal(T0, detail.Timer.TurnStartedAt);
        Assert.Equal(T0.AddSeconds(120), detail.Timer.Deadline);
        Assert.Equal(120, detail.Timer.RemainingSeconds!.Value, precision: 3);
        Assert.False(detail.Timer.IsInWarning);
    }

    [Fact]
    public async Task Remaining_time_is_computed_from_the_persisted_anchor_not_a_countdown()
    {
        var detail = await PositionDraftAsync();
        var draft = await DraftAsync(detail.Summary.Id);

        // Any evaluator — this server, a restarted one, a refreshed client — derives the same remaining
        // time from the same persisted state and instant.
        var first = DraftTimer.Describe(draft, T0.AddSeconds(50));
        var second = DraftTimer.Describe(draft, T0.AddSeconds(50));
        Assert.Equal(70, first.RemainingSeconds!.Value, precision: 3);
        Assert.Equal(first, second);

        // Without an instant to measure against, the persisted facts still project (deadline intact).
        var noNow = DraftTimer.Describe(draft, now: null);
        Assert.True(noNow.IsTimed);
        Assert.Null(noNow.RemainingSeconds);
        Assert.Equal(T0.AddSeconds(120), noNow.Deadline);
    }

    [Fact]
    public async Task The_warning_state_begins_at_fifteen_seconds()
    {
        var detail = await PositionDraftAsync();
        var draft = await DraftAsync(detail.Summary.Id);

        Assert.False(DraftTimer.Describe(draft, T0.AddSeconds(104.9)).IsInWarning); // 15.1s left
        Assert.True(DraftTimer.Describe(draft, T0.AddSeconds(105)).IsInWarning);    // exactly 15s left
        Assert.Equal(0, DraftTimer.Describe(draft, T0.AddSeconds(500)).RemainingSeconds!.Value); // clamped
    }

    [Fact]
    public async Task An_accepted_pick_restarts_the_clock_for_the_next_turn()
    {
        var detail = await PositionDraftAsync();
        _clock.Advance(TimeSpan.FromSeconds(30));

        var actor = detail.Turn.ActiveTeamMemberUserIds[0];
        var draft = await DraftAsync(detail.Summary.Id);
        var footballer = await BestAvailableAsync(draft, detail.Turn.ActiveSlotPosition);
        var next = await Pick().Handle(new SubmitPickCommand(detail.Summary.Id, footballer.Id, detail.Summary.Version, actor), default);

        Assert.Equal(T0.AddSeconds(30), next.Timer.TurnStartedAt);
        Assert.Equal(120, next.Timer.RemainingSeconds!.Value, precision: 3);
    }

    [Fact]
    public async Task The_club_round_is_untimed()
    {
        var opened = await ClubRoundAsync();
        var draft = await DraftAsync(opened.Summary.Id);

        Assert.Null(draft.TurnStartedAt);
        var timer = DraftTimer.Describe(draft, _clock.GetUtcNow());
        Assert.False(timer.IsTimed);
        Assert.Null(timer.Deadline);
    }

    // --- Expiry auto-pick (deterministic, exactly once) -----------------------------------------------

    [Fact]
    public async Task Expiry_auto_picks_the_deterministic_best_exactly_once()
    {
        var detail = await PositionDraftAsync();
        var draft = await DraftAsync(detail.Summary.Id);
        var expected = await BestAvailableAsync(draft, detail.Turn.ActiveSlotPosition);
        var activeTeamId = detail.Turn.ActiveTeamId!.Value;

        _clock.Advance(TimeSpan.FromSeconds(121));
        await Expiry().CatchUpAsync(detail.Summary.Id, default);
        await Expiry().CatchUpAsync(detail.Summary.Id, default); // a second trigger must not double-pick

        draft = await DraftAsync(detail.Summary.Id);
        var autoPicks = draft.Events.Where(evt => evt.Type == DraftEventType.PickAutoSelected).ToArray();
        var pick = Assert.Single(draft.Picks, candidate => candidate.SlotOrder == 1);

        Assert.Single(autoPicks);
        Assert.Null(autoPicks[0].ActorUserId);                       // a system action, not a human pick
        Assert.NotNull(autoPicks[0].Reason);                          // audited: why the server picked
        Assert.Equal(expected.Id, pick.FootballerId);                 // highest overall → name → id
        Assert.Equal(activeTeamId, pick.DraftTeamId);
        Assert.Null(pick.PickedByParticipantId);

        // The next turn's clock anchors at the expired deadline, not "now": the missed turn consumed
        // exactly its 120 seconds.
        Assert.Equal(T0.AddSeconds(120), draft.TurnStartedAt);
    }

    [Fact]
    public async Task A_cold_catch_up_applies_one_pick_per_missed_turn()
    {
        var detail = await PositionDraftAsync();

        // The instance "slept" through two full turns plus a bit of a third.
        _clock.Advance(TimeSpan.FromSeconds(120 * 2 + 30));
        await Expiry().CatchUpAsync(detail.Summary.Id, default);

        var draft = await DraftAsync(detail.Summary.Id);
        Assert.Equal(2, draft.Events.Count(evt => evt.Type == DraftEventType.PickAutoSelected));
        Assert.Equal(2, draft.Picks.Count(pick => pick.SlotOrder >= 1));
        Assert.Equal(T0.AddSeconds(240), draft.TurnStartedAt); // anchors cascaded by exactly 120s each
        Assert.Equal(DraftStatus.PositionDraft, draft.Status);
    }

    [Fact]
    public async Task A_submission_after_expiry_loses_to_the_auto_pick_as_a_conflict()
    {
        var detail = await PositionDraftAsync();
        var actor = detail.Turn.ActiveTeamMemberUserIds[0];
        var draft = await DraftAsync(detail.Summary.Id);
        var footballer = await BestAvailableAsync(draft, detail.Turn.ActiveSlotPosition);

        _clock.Advance(TimeSpan.FromSeconds(150));

        await Assert.ThrowsAsync<ConflictAppException>(() =>
            Pick().Handle(new SubmitPickCommand(detail.Summary.Id, footballer.Id, detail.Summary.Version, actor), default));

        // The timer won: the auto-pick stands and the turn has advanced.
        draft = await DraftAsync(detail.Summary.Id);
        Assert.Contains(draft.Events, evt => evt.Type == DraftEventType.PickAutoSelected);
    }

    [Fact]
    public async Task Expiring_the_final_slot_completes_the_draft()
    {
        var detail = await PositionDraftAsync();
        var id = detail.Summary.Id;

        // Fill every slot but the last with live picks.
        while (true)
        {
            detail = await SnapshotAsync(id);
            var openSlots = detail.Slots.Count(slot => slot.Order >= 1) * detail.Teams.Count
                - detail.Picks.Count(pick => pick.SlotOrder >= 1);
            if (openSlots <= 1)
            {
                break;
            }

            var actor = detail.Turn.ActiveTeamMemberUserIds[0];
            var draft = await DraftAsync(id);
            var position = detail.Turn.SlotAcceptsAnyPosition ? null : detail.Turn.ActiveSlotPosition;
            var footballer = await BestAvailableAsync(draft, position);
            detail = await Pick().Handle(new SubmitPickCommand(id, footballer.Id, detail.Summary.Version, actor), default);
        }

        _clock.Advance(TimeSpan.FromSeconds(121));
        await Expiry().CatchUpAsync(id, default);

        var completed = await DraftAsync(id);
        Assert.Equal(DraftStatus.Completed, completed.Status);
        Assert.Null(completed.TurnStartedAt); // the clock stopped with the draft
        Assert.Contains(completed.Events, evt => evt.Type == DraftEventType.PickAutoSelected);
        Assert.Contains(completed.Events, evt => evt.Type == DraftEventType.DraftCompleted);
    }

    // --- Pause / resume / cancel (host controls) ------------------------------------------------------

    [Fact]
    public async Task Pause_freezes_the_clock_and_resume_shifts_the_anchor()
    {
        var detail = await PositionDraftAsync();
        var id = detail.Summary.Id;

        _clock.Advance(TimeSpan.FromSeconds(30)); // 90 seconds remain
        var paused = await Pause().Handle(new PauseDraftCommand(id, "Connection troubles", detail.Summary.Version, _host), default);
        Assert.Equal("Paused", paused.Summary.Status);
        Assert.True(paused.Timer.IsPaused);
        Assert.Equal(90, paused.Timer.RemainingSeconds!.Value, precision: 3);

        // A long pause elapses no draft time — and never triggers the auto-pick.
        _clock.Advance(TimeSpan.FromMinutes(30));
        await Expiry().CatchUpAsync(id, default);
        var draft = await DraftAsync(id);
        Assert.DoesNotContain(draft.Events, evt => evt.Type == DraftEventType.PickAutoSelected);
        Assert.Equal(90, DraftTimer.Describe(draft, _clock.GetUtcNow()).RemainingSeconds!.Value, precision: 3);

        var resumed = await Resume().Handle(new ResumeDraftCommand(id, paused.Summary.Version, _host), default);
        Assert.Equal("PositionDraft", resumed.Summary.Status);
        Assert.False(resumed.Timer.IsPaused);
        Assert.Equal(90, resumed.Timer.RemainingSeconds!.Value, precision: 3);

        // The shifted anchor expires exactly 90 seconds after the resume.
        _clock.Advance(TimeSpan.FromSeconds(91));
        await Expiry().CatchUpAsync(id, default);
        draft = await DraftAsync(id);
        Assert.Contains(draft.Events, evt => evt.Type == DraftEventType.PickAutoSelected);
    }

    [Fact]
    public async Task Resume_returns_to_the_state_the_draft_paused_from()
    {
        var opened = await ClubRoundAsync();
        var id = opened.Summary.Id;

        var paused = await Pause().Handle(new PauseDraftCommand(id, "Break", opened.Summary.Version, _host), default);
        Assert.Equal("Paused", paused.Summary.Status);

        var resumed = await Resume().Handle(new ResumeDraftCommand(id, paused.Summary.Version, _host), default);
        Assert.Equal("ClubSelection", resumed.Summary.Status); // back where it paused from — still untimed
        Assert.False(resumed.Timer.IsTimed);
    }

    [Fact]
    public async Task Pause_and_cancel_are_host_only_and_cancel_preserves_history()
    {
        var detail = await PositionDraftAsync();
        var id = detail.Summary.Id;

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Pause().Handle(new PauseDraftCommand(id, "Not my draft", detail.Summary.Version, _guest), default));
        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Cancel().Handle(new CancelDraftCommand(id, "Not my draft", detail.Summary.Version, _guest), default));

        var before = (await DraftAsync(id)).Events.Count;
        var cancelled = await Cancel().Handle(new CancelDraftCommand(id, "Session abandoned by agreement", detail.Summary.Version, _host), default);

        Assert.Equal("Cancelled", cancelled.Summary.Status);
        var draft = await DraftAsync(id);
        Assert.Equal(before + 1, draft.Events.Count); // cancellation only appended
        var cancelEvent = draft.Events.Single(evt => evt.Type == DraftEventType.DraftCancelled);
        Assert.Equal("Session abandoned by agreement", cancelEvent.Reason);
        Assert.Null(draft.TurnStartedAt);
    }

    [Fact]
    public void Pause_and_cancel_require_a_reason()
    {
        var pause = new PauseDraftCommandValidator()
            .Validate(new PauseDraftCommand(Guid.NewGuid(), " ", 1, Guid.NewGuid()));
        var cancel = new CancelDraftCommandValidator()
            .Validate(new CancelDraftCommand(Guid.NewGuid(), "", 1, Guid.NewGuid()));

        Assert.False(pause.IsValid);
        Assert.False(cancel.IsValid);
    }

    // --- Admin recovery (§9.7: separately permissioned, compensating) ---------------------------------

    [Fact]
    public async Task Admin_recovery_is_admin_only_and_appends_a_compensating_event()
    {
        var detail = await PositionDraftAsync();
        var id = detail.Summary.Id;

        // Even the host cannot apply recovery — it is separately permissioned (§9.7).
        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Recover().Handle(new ApplyAdminRecoveryCommand(id, "Stuck turn", RestartTurnClock: true, detail.Summary.Version, _host), default));

        _clock.Advance(TimeSpan.FromSeconds(100)); // 20 seconds remain
        var admin = _identity.Add("Admin", role: UserRole.Admin).Id;
        var eventsBefore = (await DraftAsync(id)).Events.Select(evt => evt.Id).ToArray();

        var recovered = await Recover().Handle(
            new ApplyAdminRecoveryCommand(id, "Turn clock restored after an outage", RestartTurnClock: true, detail.Summary.Version, admin, ActorIsAdmin: true), default);

        var draft = await DraftAsync(id);
        var recovery = draft.Events.Single(evt => evt.Type == DraftEventType.AdminRecoveryApplied);
        Assert.Equal("Turn clock restored after an outage", recovery.Reason);
        Assert.Equal(admin, recovery.ActorUserId);
        // The original history was never edited or deleted — only appended to.
        Assert.All(eventsBefore, eventId => Assert.Contains(draft.Events, evt => evt.Id == eventId));
        // The active turn got a fresh full clock.
        Assert.Equal(120, recovered.Timer.RemainingSeconds!.Value, precision: 3);
    }
}
