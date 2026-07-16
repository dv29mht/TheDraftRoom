using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Proves the PR-16 done-whens against a real PostgreSQL server: the timer anchors persist
/// (turn_started_at/paused_at survive a "restart", so a fresh scope computes the SAME remaining time from
/// the stored state); pause/resume round-trips through the database with paused time never elapsing; and
/// two CONCURRENT expiry triggers yield exactly ONE auto-pick — the version token and the unique
/// (team, slot)/(draft, footballer) indexes settle the race transactionally. Tests share one database, so
/// every assertion is scoped to the draft it created; helpers from <see cref="DraftClubAndPositionDbTests"/>
/// keep them order-independent. Skips when Docker is absent.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DraftTimerDbTests(PostgresFixture fixture)
{
    /// <summary>A settable clock for driving expiry against a real database.</summary>
    private sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    /// <summary>Drives a fresh 1v1 draft into the position draft (clubs chosen, first turn clocked).</summary>
    private static async Task<DraftDetail> PositionDraftAsync(IServiceScope scope, ISender sender, Guid host)
    {
        var detail = await DraftClubAndPositionDbTests.ClubRoundAsync(scope, sender, host);
        var id = detail.Summary.Id;
        var pinned = detail.Summary.PinnedDatasetVersionId;
        var catalog = scope.ServiceProvider.GetRequiredService<IDraftCatalog>();

        for (var team = 0; team < 2; team++)
        {
            var actor = detail.Turn.ActiveTeamMemberUserIds[0];
            var takenClubs = detail.Teams.Where(t => t.SelectedClubId is not null).Select(t => t.SelectedClubId!.Value).ToHashSet();
            var club = (await catalog.ListFiveStarClubsAsync(pinned, default)).First(c => !takenClubs.Contains(c.Id));
            var taken = detail.Picks.Select(p => p.FootballerId).ToHashSet();
            var held = (await catalog.ListFootballersAsync(pinned, new CatalogFootballerFilter(ClubId: club.Id), default))
                .First(f => !taken.Contains(f.Id));
            detail = await sender.Send(new SelectClubAndProtectCommand(id, club.Id, held.Id, detail.Summary.Version, actor));
        }

        detail = await sender.Send(new OpenPositionDraftCommand(id, detail.Summary.Version, host));
        Assert.Equal("PositionDraft", detail.Summary.Status);
        return detail;
    }

    /// <summary>An expiry service over the given scope's stores, driven by the supplied test clock.</summary>
    private static DraftExpiryService ExpiryFor(IServiceScope scope, TimeProvider clock) => new(
        scope.ServiceProvider.GetRequiredService<IDraftStore>(),
        scope.ServiceProvider.GetRequiredService<IDraftCatalog>(),
        scope.ServiceProvider.GetRequiredService<IIdentityService>(),
        scope.ServiceProvider.GetRequiredService<ITransactionRunner>(),
        new NullDraftNotifier(),
        clock,
        scope.ServiceProvider.GetRequiredService<DraftParticipantNotifier>());

    [SkippableFact]
    public async Task The_timer_anchor_persists_and_a_restarted_scope_computes_the_same_remaining_time()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        DateTimeOffset anchor;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var host = await DraftClubAndPositionDbTests.HostIdAsync(scope);
            var detail = await PositionDraftAsync(scope, sender, host);
            draftId = detail.Summary.Id;

            Assert.True(detail.Timer.IsTimed);
            Assert.NotNull(detail.Timer.TurnStartedAt);
            anchor = detail.Timer.TurnStartedAt!.Value;
            Assert.Equal(anchor.AddSeconds(detail.Timer.PickTimerSeconds), detail.Timer.Deadline);
        }

        // A brand-new scope (fresh DbContext — the "restarted server") reads the persisted anchor and, for
        // the same instant, derives exactly the same remaining time. No in-process countdown survives here.
        var probe = anchor.AddSeconds(45);
        double RemainingIn(IServiceScope scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var stored = db.Drafts.AsNoTracking().First(d => d.Id == draftId);
            // turn_started_at persisted — compared with tolerance because timestamptz keeps microsecond
            // precision while DateTimeOffset carries 100ns ticks.
            Assert.NotNull(stored.TurnStartedAt);
            Assert.Equal(anchor, stored.TurnStartedAt!.Value, TimeSpan.FromMilliseconds(1));
            Assert.Null(stored.PausedAt);
            return DraftTimer.Describe(stored, probe).RemainingSeconds!.Value;
        }

        using (var first = api.Services.CreateScope())
        using (var second = api.Services.CreateScope())
        {
            var remainingFirst = RemainingIn(first);
            var remainingSecond = RemainingIn(second);
            Assert.Equal(75, remainingFirst, precision: 3);
            Assert.Equal(remainingFirst, remainingSecond);
        }
    }

    [SkippableFact]
    public async Task Pause_and_resume_persist_and_paused_time_never_elapses()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        using var scope = api.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var host = await DraftClubAndPositionDbTests.HostIdAsync(scope);
        var detail = await PositionDraftAsync(scope, sender, host);
        var id = detail.Summary.Id;

        var paused = await sender.Send(new PauseDraftCommand(id, "Half-time break", detail.Summary.Version, host));
        Assert.Equal("Paused", paused.Summary.Status);
        Assert.True(paused.Timer.IsPaused);

        var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
        var stored = await db.Drafts.AsNoTracking().FirstAsync(d => d.Id == id);
        Assert.NotNull(stored.PausedAt); // paused_at persisted
        var frozenRemaining = DraftTimer.Describe(stored, stored.PausedAt!.Value.AddMinutes(30)).RemainingSeconds!.Value;
        Assert.Equal(
            (stored.TurnDeadline!.Value - stored.PausedAt!.Value).TotalSeconds,
            frozenRemaining,
            precision: 3); // measured against the freeze instant, not "now" — paused time never elapses

        var resumed = await sender.Send(new ResumeDraftCommand(id, paused.Summary.Version, host));
        Assert.Equal("PositionDraft", resumed.Summary.Status);
        var restored = await db.Drafts.AsNoTracking().FirstAsync(d => d.Id == id);
        Assert.Null(restored.PausedAt);
        Assert.True(restored.TurnStartedAt > stored.TurnStartedAt); // the anchor shifted by the pause length
    }

    [SkippableFact]
    public async Task Two_concurrent_expiry_triggers_yield_exactly_one_auto_pick()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        int versionBefore;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var host = await DraftClubAndPositionDbTests.HostIdAsync(scope);
            var detail = await PositionDraftAsync(scope, sender, host);
            draftId = detail.Summary.Id;
            versionBefore = detail.Summary.Version;
        }

        // Both triggers see the turn as expired (a test clock 121s ahead of the real anchor) and race the
        // same auto-pick from two independent scopes — two DbContexts, two transactions, like the sweep
        // and a board read colliding. The per-draft gate serializes in-process collisions and the version
        // token + unique indexes settle any cross-transaction race; either way exactly one pick commits
        // and the loser sees the turn already advanced.
        var clock = new TestClock(DateTimeOffset.UtcNow);
        clock.Advance(TimeSpan.FromSeconds(121));

        using (var first = api.Services.CreateScope())
        using (var second = api.Services.CreateScope())
        {
            await Task.WhenAll(
                ExpiryFor(first, clock).CatchUpAsync(draftId, default),
                ExpiryFor(second, clock).CatchUpAsync(draftId, default));
        }

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts.AsNoTracking()
                .Include(d => d.Picks).Include(d => d.Events)
                .FirstAsync(d => d.Id == draftId);

            var autoEvents = draft.Events.Where(e => e.Type == DraftEventType.PickAutoSelected).ToArray();
            Assert.Single(autoEvents);                                     // ONE event…
            Assert.Single(draft.Picks, pick => pick.SlotOrder == 1);       // …one pick for the expired slot
            Assert.Null(autoEvents[0].ActorUserId);                        // recorded as a system action
            Assert.Equal(versionBefore + 1, draft.Version);                // one version bump, not two
            Assert.Equal(DraftStatus.PositionDraft, draft.Status);
        }
    }
}
