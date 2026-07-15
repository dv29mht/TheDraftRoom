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
/// Proves the PR-10 definition of done against a real PostgreSQL server: transitions persist and the
/// current state rebuilds from the append-only history; a stale version conflicts (→409) with no partial
/// write; an illegal transition is rejected with no partial write; the <c>version</c> concurrency token
/// blocks a lost update under a genuine race; and starting a draft snapshots the roster template and
/// pins the active dataset version. Every test skips cleanly when Docker is unavailable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DraftAggregateDbTests(PostgresFixture fixture)
{
    private static readonly Guid Host = Guid.NewGuid();

    [SkippableFact]
    public async Task Transitions_persist_and_state_rebuilds_from_history()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var created = await sender.Send(new CreateDraftCommand($"DB Draft {Guid.NewGuid():N}", "1v1", Host));
            draftId = created.Id;
            await sender.Send(new TransitionDraftCommand(draftId, "Lobby", "ParticipantInvited", 1, Host));
            await sender.Send(new TransitionDraftCommand(draftId, "TeamFormation", "TeamsFormed", 2, Host));
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
            var created = await sender.Send(new CreateDraftCommand($"DB Stale {Guid.NewGuid():N}", "1v1", Host));
            draftId = created.Id;
            await sender.Send(new TransitionDraftCommand(draftId, "Lobby", "ParticipantInvited", 1, Host)); // now version 2
        }

        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await Assert.ThrowsAsync<ConflictAppException>(() =>
                sender.Send(new TransitionDraftCommand(draftId, "TeamFormation", "TeamsFormed", 1, Host)));
        }

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts.Include(d => d.Events).FirstAsync(d => d.Id == draftId);
            Assert.Equal(DraftStatus.Lobby, draft.Status); // unchanged
            Assert.Equal(2, draft.Version);                 // unchanged
            Assert.Equal(2, draft.Events.Count);            // no event appended by the rejected move
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
            var created = await sender.Send(new CreateDraftCommand($"DB Illegal {Guid.NewGuid():N}", "1v1", Host));
            draftId = created.Id;

            await Assert.ThrowsAsync<ValidationAppException>(() =>
                sender.Send(new TransitionDraftCommand(draftId, "PositionDraft", "PickAccepted", 1, Host)));
        }

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts.Include(d => d.Events).FirstAsync(d => d.Id == draftId);
            Assert.Equal(DraftStatus.Draft, draft.Status);
            Assert.Equal(1, draft.Version);
            Assert.Single(draft.Events);
        }
    }

    [SkippableFact]
    public async Task The_version_concurrency_token_blocks_a_lost_update()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var created = await sender.Send(new CreateDraftCommand($"DB Race {Guid.NewGuid():N}", "1v1", Host));
            draftId = created.Id;
        }

        // Two independent units of work both load version 1 and try to advance it. The first commit wins;
        // the second must lose on the version token, even though it sent no stale expected-version itself.
        using var scopeA = api.Services.CreateScope();
        using var scopeB = api.Services.CreateScope();
        var storeA = scopeA.ServiceProvider.GetRequiredService<IDraftStore>();
        var storeB = scopeB.ServiceProvider.GetRequiredService<IDraftStore>();

        var draftA = await storeA.FindAsync(draftId, default);
        var draftB = await storeB.FindAsync(draftId, default);

        draftA!.Transition(DraftStatus.Lobby, DraftEventType.ParticipantInvited, Host);
        await storeA.SaveChangesAsync(default);

        draftB!.Transition(DraftStatus.Lobby, DraftEventType.ParticipantInvited, Host);
        await Assert.ThrowsAsync<ConflictAppException>(() => storeB.SaveChangesAsync(default));

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var draft = await db.Drafts.Include(d => d.Events).FirstAsync(d => d.Id == draftId);
            Assert.Equal(2, draft.Version);         // exactly one accepted transition
            Assert.Equal(2, draft.Events.Count);    // only one new event survived
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
            var templates = scope.ServiceProvider.GetRequiredService<IRosterTemplateService>();
            // Tests in the "postgres" collection run sequentially, so the active template captured here is
            // the one Start will snapshot a moment later.
            var activeTemplate = await templates.GetActiveAsync(default);
            Assert.NotNull(activeTemplate);
            expectedSlotCount = activeTemplate!.Slots.Count;

            var created = await sender.Send(new CreateDraftCommand($"DB Start {Guid.NewGuid():N}", "1v1", Host));
            draftId = created.Id;
            await sender.Send(new TransitionDraftCommand(draftId, "Lobby", "ParticipantInvited", 1, Host));
            await sender.Send(new TransitionDraftCommand(draftId, "TeamFormation", "TeamsFormed", 2, Host));
            await sender.Send(new TransitionDraftCommand(draftId, "ReadyCheck", "ParticipantReadied", 3, Host));

            var started = await sender.Send(new StartDraftCommand(draftId, 4, Host));
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
            Assert.Equal(expectedSlotCount, draft.Slots.Count);   // template ordered slots snapshotted
            Assert.NotNull(draft.PinnedDatasetVersionId);
            Assert.Contains(draft.Events, evt => evt.Type == DraftEventType.DraftStarted);
        }
    }
}
