using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using FcDraft.Application.Features.Overview;
using FcDraft.Domain.Entities;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Proves the PR-24 admin Overview query runs against real PostgreSQL — specifically that the new
/// EF reader aggregations (<see cref="IDraftEventReader.CountByTypeAsync"/> grouping the enum-mapped
/// <c>draft_events.type</c> column, and <see cref="IEmailOutboxReader.GetStatusTalliesAsync"/>) translate
/// to SQL rather than throwing at runtime — which the in-memory integration test cannot catch.
/// Skips cleanly when Docker is unavailable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminOverviewDbTests(PostgresFixture fixture)
{
    [SkippableFact]
    public async Task Overview_aggregates_users_drafts_and_events_against_postgres()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await using var api = new PostgresApiFactory(fixture.ConnectionString!);

        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var host = await identity.FindByEmailAsync(SeededAccounts.PlayerEmail, default);
            await sender.Send(new CreateDraftCommand($"Overview DB {Guid.NewGuid():N}", "1v1", host!.Id, null, []));
        }

        using (var scope = api.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            // The query must execute (proving the EF GroupBy translations) and reflect the created lobby.
            var overview = await sender.Send(new GetAdminOverviewQuery());

            Assert.True(overview.Users.Total >= 1);
            Assert.True(overview.Drafts.Total >= 1);
            Assert.True(overview.Drafts.ByStatus.GetValueOrDefault(nameof(DraftStatus.Lobby)) >= 1);
            Assert.True(overview.Engagement.Created >= 1);
            Assert.InRange(overview.Engagement.CompletionRate, 0d, 1d);
            // GetStatusTalliesAsync ran without throwing; tallies are non-negative.
            Assert.True(overview.Email.Failed >= 0);
        }
    }
}
