using System.Net.Http.Json;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Proves the PR-21 done-whens against a real PostgreSQL server: a confirmed announcement commits its
/// campaign record, in-app notifications, and THROTTLED campaign-stamped outbox rows in one
/// transaction; the outbox worker drains only the due batch (bulk sends never burst); delivery
/// tallies are observable and the whole campaign trail survives an API restart; and the append-only
/// draft-event trail is byte-for-byte untouched by later admin operations — recovery only ever
/// appends compensating events. Skips cleanly when Docker is unavailable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AnnouncementAndAuditDbTests(PostgresFixture fixture)
{
    private sealed record PreviewBody(int RecipientCount, int EmailRecipientCount, int OptedOutCount, string AudienceLabel);
    private sealed record PreviewEnvelope(PreviewBody Preview, string SenderName, string? SenderEmail, bool EmailConfigured);
    private sealed record AnnouncementRow(
        Guid Id, string Subject, string Audience, string AudienceLabel, int RecipientCount, int EmailCount,
        int OptedOutCount, string RequestedByEmail, DateTimeOffset RequestedAt,
        int EmailsPending, int EmailsSent, int EmailsFailed);

    private static async Task<HttpClient> AdminAsync(PostgresApiFactory factory)
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return factory.CreateClient().WithBearer(admin.AccessToken);
    }

    private static async Task<int> ProcessOutboxAsync(PostgresApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IEmailOutboxProcessor>();
        return await processor.ProcessDueAsync(default);
    }

    [SkippableFact]
    public async Task A_bulk_announcement_is_throttled_through_the_outbox_and_its_campaign_survives_a_restart()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var factory = new PostgresApiFactory(fixture.ConnectionString!, useEmailOutbox: true);
        var admin = await AdminAsync(factory);

        // Build an audience large enough to need two delivery windows (batch size 20).
        for (var i = 0; i < 22; i++)
        {
            var create = await admin.PostAsJsonAsync("/api/users",
                new { email = $"bulk-{Guid.NewGuid():N}@draftroom.test", displayName = $"Bulk {i:00}" });
            create.EnsureSuccessStatusCode();
        }

        // Drain the invitation backlog so the next processed batch is purely the announcement's.
        while (await ProcessOutboxAsync(factory) > 0)
        {
        }

        var preview = (await (await admin.PostAsJsonAsync("/api/admin/announcements/preview",
            new { subject = "DB bulk", body = "Throttle proof.", audience = "all", draftId = (Guid?)null }))
            .Content.ReadFromJsonAsync<PreviewEnvelope>())!;
        Assert.True(preview.Preview.EmailRecipientCount > 20, "audience must span two throttle windows");

        var sent = (await (await admin.PostAsJsonAsync("/api/admin/announcements",
            new { subject = "DB bulk", body = "Throttle proof.", audience = "all", draftId = (Guid?)null, confirmedRecipientCount = preview.Preview.RecipientCount }))
            .Content.ReadFromJsonAsync<AnnouncementRow>())!;
        Assert.Equal(preview.Preview.EmailRecipientCount, sent.EmailCount);
        Assert.Equal(sent.EmailCount, sent.EmailsPending); // durable branch: everything queued, nothing inline

        // The §9.8 throttle, verified at the rows: campaign-stamped, staggered across 15s windows.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var rows = await db.EmailOutbox.AsNoTracking()
                .Where(row => row.CampaignId == sent.Id)
                .ToArrayAsync();
            Assert.Equal(sent.EmailCount, rows.Length);
            var windows = rows.GroupBy(row => row.NextAttemptAt).OrderBy(group => group.Key).ToArray();
            Assert.Equal(2, windows.Length);
            Assert.Equal(20, windows[0].Count());
            Assert.Equal(sent.EmailCount - 20, windows[1].Count());
            Assert.Equal(TimeSpan.FromSeconds(15), windows[1].Key - windows[0].Key);
        }

        // One worker pass delivers ONLY the due window — the second window is not due yet.
        await ProcessOutboxAsync(factory);
        var afterFirstPass = (await admin.GetFromJsonAsync<List<AnnouncementRow>>("/api/admin/announcements"))!
            .Single(row => row.Id == sent.Id);
        Assert.Equal(20, afterFirstPass.EmailsSent);
        Assert.Equal(sent.EmailCount - 20, afterFirstPass.EmailsPending);

        // Restart the API (fresh host, same database): the campaign record and tallies persist.
        await factory.DisposeAsync();
        await using var restarted = new PostgresApiFactory(fixture.ConnectionString!, useEmailOutbox: true);
        var adminAfterRestart = await AdminAsync(restarted);
        var survived = (await adminAfterRestart.GetFromJsonAsync<List<AnnouncementRow>>("/api/admin/announcements"))!
            .Single(row => row.Id == sent.Id);
        Assert.Equal("DB bulk", survived.Subject);
        Assert.Equal(20, survived.EmailsSent);
        Assert.Equal(sent.EmailCount - 20, survived.EmailsPending);

        // Bring the second window forward; the restarted worker path drains it to completion.
        using (var scope = restarted.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            await db.EmailOutbox
                .Where(row => row.CampaignId == sent.Id && row.SentAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    row => row.NextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await ProcessOutboxAsync(restarted);
        var drained = (await adminAfterRestart.GetFromJsonAsync<List<AnnouncementRow>>("/api/admin/announcements"))!
            .Single(row => row.Id == sent.Id);
        Assert.Equal(sent.EmailCount, drained.EmailsSent);
        Assert.Equal(0, drained.EmailsPending);
        Assert.Equal(0, drained.EmailsFailed);
    }

    [SkippableFact]
    public async Task Admin_operations_append_compensating_events_and_never_touch_the_existing_trail()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);

        Guid draftId;
        Guid adminId;
        int versionAfterSetup;
        using (var scope = factory.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var host = await DraftClubAndPositionDbTests.HostIdAsync(scope);
            var detail = await DraftClubAndPositionDbTests.ClubRoundAsync(scope, sender, host);
            draftId = detail.Summary.Id;
            versionAfterSetup = detail.Summary.Version;

            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            adminId = (await identity.FindByEmailAsync(SeededAccounts.AdminEmail, default))!.Id;
        }

        // The immutability baseline comes from a FRESH scope so it reads committed database state.
        (Guid Id, int Sequence, string Type, Guid? Actor, string? Reason, DateTimeOffset CreatedAt, int Version)[] baseline;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            baseline = (await db.DraftEvents.AsNoTracking()
                    .Where(evt => evt.DraftId == draftId)
                    .OrderBy(evt => evt.Sequence)
                    .ToArrayAsync())
                .Select(evt => (evt.Id, evt.Sequence, evt.Type.ToString(), evt.ActorUserId, evt.Reason, evt.CreatedAt, evt.Version))
                .ToArray();
            Assert.NotEmpty(baseline);
        }

        // An admin who is neither host nor participant pauses (required reason) and resumes.
        using (var scope = factory.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var paused = await sender.Send(new PauseDraftCommand(
                draftId, "DB pause proof", versionAfterSetup, adminId, ActorIsAdmin: true));
            Assert.Equal("Paused", paused.Summary.Status);
            var resumed = await sender.Send(new ResumeDraftCommand(
                draftId, paused.Summary.Version, adminId, ActorIsAdmin: true));
            Assert.Equal("ClubSelection", resumed.Summary.Status); // back to the round it paused from
        }

        // A fresh scope re-reads the trail: the baseline is byte-for-byte intact, the new events are
        // appended after it with the admin attributed, and the sequence stays gap-free.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var events = await db.DraftEvents.AsNoTracking()
                .Where(evt => evt.DraftId == draftId)
                .OrderBy(evt => evt.Sequence)
                .ToArrayAsync();

            var prefix = events.Take(baseline.Length)
                .Select(evt => (evt.Id, evt.Sequence, evt.Type.ToString(), evt.ActorUserId, evt.Reason, evt.CreatedAt, evt.Version))
                .ToArray();
            Assert.Equal(baseline, prefix);

            Assert.Equal(baseline.Length + 2, events.Length);
            Assert.Equal(Enumerable.Range(1, events.Length), events.Select(evt => evt.Sequence)); // gap-free
            var pause = events[^2];
            Assert.Equal("DraftPaused", pause.Type.ToString());
            Assert.Equal(adminId, pause.ActorUserId);
            Assert.Equal("DB pause proof", pause.Reason);
            Assert.Equal("DraftResumed", events[^1].Type.ToString());
        }

        // The audit view over the same database applies the draft/type filters.
        var admin = await AdminAsync(factory);
        var audit = (await admin.GetFromJsonAsync<List<AuditRowLite>>(
            $"/api/admin/audit/draft-events?draftId={draftId}&type=DraftPaused"))!;
        var row = Assert.Single(audit);
        Assert.Equal(adminId, row.ActorUserId);
        Assert.Equal("DB pause proof", row.Reason);
    }

    private sealed record AuditRowLite(Guid DraftId, int Sequence, string Type, Guid? ActorUserId, string? Reason);
}
