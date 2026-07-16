using FcDraft.Application.Common.Exceptions;
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
/// Proves the PR-10 aggregate guarantees against a real PostgreSQL server, now that a lobby is created with
/// its host (PR-11): transitions persist and the current state rebuilds from history; a stale version
/// conflicts (→409) with no partial write; an illegal transition is rejected with no partial write; the
/// <c>version</c> concurrency token blocks a lost update under a genuine race; and starting a draft
/// snapshots the bound roster template and pins the active dataset version. Every test skips cleanly when
/// Docker is unavailable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DraftAggregateDbTests(PostgresFixture fixture)
{
    private static async Task<Guid> HostIdAsync(IServiceScope scope)
    {
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var host = await identity.FindByEmailAsync(SeededAccounts.PlayerEmail, default);
        return host!.Id;
    }

    [SkippableFact]
    public async Task Transitions_persist_and_state_rebuilds_from_history()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var host = await HostIdAsync(scope);
            var created = await sender.Send(new CreateDraftCommand($"DB Draft {Guid.NewGuid():N}", "1v1", host));
            draftId = created.Summary.Id;
            await sender.Send(new TransitionDraftCommand(draftId, "TeamFormation", "TeamsFormed", 2, host));
        }

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts.Include(d => d.Events).FirstAsync(d => d.Id == draftId);

            Assert.Equal(DraftStatus.TeamFormation, draft.Status);
            Assert.Equal(3, draft.Version);

            var snapshot = DraftStateProjection.Replay(draft.Events);
            Assert.Equal(draft.Status, snapshot.Status);
            Assert.Equal(draft.Version, snapshot.Version);
            Assert.Equal(3, snapshot.EventCount);
        }
    }

    [SkippableFact]
    public async Task A_stale_version_transition_conflicts_without_a_partial_write()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var host = await HostIdAsync(scope);
            var created = await sender.Send(new CreateDraftCommand($"DB Stale {Guid.NewGuid():N}", "1v1", host));
            draftId = created.Summary.Id;
            await sender.Send(new TransitionDraftCommand(draftId, "TeamFormation", "TeamsFormed", 2, host)); // now version 3
        }

        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var host = await HostIdAsync(scope);
            await Assert.ThrowsAsync<ConflictAppException>(() =>
                sender.Send(new TransitionDraftCommand(draftId, "ReadyCheck", "ParticipantReadied", 2, host)));
        }

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts.Include(d => d.Events).FirstAsync(d => d.Id == draftId);
            Assert.Equal(DraftStatus.TeamFormation, draft.Status); // unchanged
            Assert.Equal(3, draft.Version);                         // unchanged
            Assert.Equal(3, draft.Events.Count);                    // no event appended by the rejected move
        }
    }

    [SkippableFact]
    public async Task An_illegal_transition_is_rejected_without_a_partial_write()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var host = await HostIdAsync(scope);
            var created = await sender.Send(new CreateDraftCommand($"DB Illegal {Guid.NewGuid():N}", "1v1", host));
            draftId = created.Summary.Id;

            await Assert.ThrowsAsync<ValidationAppException>(() =>
                sender.Send(new TransitionDraftCommand(draftId, "PositionDraft", "PickAccepted", 2, host)));
        }

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts.Include(d => d.Events).FirstAsync(d => d.Id == draftId);
            Assert.Equal(DraftStatus.Lobby, draft.Status);
            Assert.Equal(2, draft.Version);
            Assert.Equal(2, draft.Events.Count);
        }
    }

    [SkippableFact]
    public async Task The_version_concurrency_token_blocks_a_lost_update()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        Guid host;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            host = await HostIdAsync(scope);
            var created = await sender.Send(new CreateDraftCommand($"DB Race {Guid.NewGuid():N}", "1v1", host));
            draftId = created.Summary.Id;
        }

        // Two independent units of work both load version 2 and try to advance it. The first commit wins;
        // the second must lose on the version token, even though it sent no stale expected-version itself.
        using var scopeA = api.Services.CreateScope();
        using var scopeB = api.Services.CreateScope();
        var storeA = scopeA.ServiceProvider.GetRequiredService<IDraftStore>();
        var storeB = scopeB.ServiceProvider.GetRequiredService<IDraftStore>();

        var draftA = await storeA.FindAsync(draftId, default);
        var draftB = await storeB.FindAsync(draftId, default);

        draftA!.Transition(DraftStatus.TeamFormation, DraftEventType.TeamsFormed, host);
        await storeA.SaveChangesAsync(default);

        draftB!.Transition(DraftStatus.TeamFormation, DraftEventType.TeamsFormed, host);
        await Assert.ThrowsAsync<ConflictAppException>(() => storeB.SaveChangesAsync(default));

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts.Include(d => d.Events).FirstAsync(d => d.Id == draftId);
            Assert.Equal(3, draft.Version);         // exactly one accepted transition on top of creation
            Assert.Equal(3, draft.Events.Count);    // only one new event survived
        }
    }

    [SkippableFact]
    public async Task Starting_a_draft_snapshots_the_template_and_pins_the_dataset_version()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        int expectedSlotCount;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var templates = scope.ServiceProvider.GetRequiredService<IRosterTemplateService>();
            var host = await HostIdAsync(scope);
            // Tests in the "postgres" collection run sequentially, so the active template captured here is
            // the one the lobby binds and Start snapshots a moment later.
            var activeTemplate = await templates.GetActiveAsync(default);
            Assert.NotNull(activeTemplate);
            expectedSlotCount = activeTemplate!.Slots.Count;

            // A real start now satisfies the §9.4 gate: a joined guest, solo teams, and both ready.
            var guest = await identity.CreateUserAsync("Start Guest", $"start-guest-{Guid.NewGuid():N}@draftroom.test", UserRole.Player, default);
            var created = await sender.Send(new CreateDraftCommand($"DB Start {Guid.NewGuid():N}", "1v1", host, null, [guest.Id]));
            draftId = created.Summary.Id;

            var joined = await sender.Send(new JoinDraftCommand(draftId, created.Summary.Version, guest.Id));
            var locked = await sender.Send(new LockLobbyCommand(draftId, joined.Summary.Version, host));
            var teams = await sender.Send(new FormTeamsCommand(draftId, null, locked.Summary.Version, host));
            var hostReady = await sender.Send(new SetReadyCommand(draftId, true, teams.Summary.Version, host));
            var guestReady = await sender.Send(new SetReadyCommand(draftId, true, hostReady.Summary.Version, guest.Id));
            var readyCheck = await sender.Send(new BeginReadyCheckCommand(draftId, guestReady.Summary.Version, host));

            var started = await sender.Send(new StartDraftCommand(draftId, readyCheck.Summary.Version, host));
            Assert.Equal("SpinnerRanking", started.Status);
            Assert.NotNull(started.PinnedDatasetVersionId); // seeded/active dataset version pinned
        }

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts
                .Include(d => d.Slots)
                .Include(d => d.Events)
                .FirstAsync(d => d.Id == draftId);

            Assert.Equal(DraftStatus.SpinnerRanking, draft.Status);
            Assert.Equal(expectedSlotCount, draft.Slots.Count);   // bound template's ordered slots snapshotted
            Assert.NotNull(draft.PinnedDatasetVersionId);
            Assert.Contains(draft.Events, evt => evt.Type == DraftEventType.DraftStarted);
        }
    }
}
