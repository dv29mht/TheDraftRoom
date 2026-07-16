using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Drafts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FcDraft.Infrastructure.Drafts;

/// <summary>
/// Belt-and-braces expiry sweep (PR-16). The read/command paths evaluate the 120s clock lazily — that is
/// the authority on a host that can scale to zero — but while the instance is warm (PR-17's open draft
/// websockets keep it warm exactly during live drafts) this worker applies overdue auto-picks within a few
/// seconds so connected clients see them without waiting for the next request. Each catch-up runs in its
/// own scope, and a lost race with a concurrent trigger is swallowed inside
/// <see cref="DraftExpiryService"/> — exactly one pick per expired turn ever commits.
/// </summary>
public sealed class DraftTimerSweepWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<DraftTimerSweepWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The sweep is opportunistic; the lazy read-path evaluation remains correct without it.
                logger.LogError(exception, "Draft timer sweep failed; will retry on the next interval");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<Guid> expired;
        using (var scope = scopeFactory.CreateScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDraftStore>();
            expired = await drafts.ListDraftIdsWithExpiredTurnsAsync(clock.GetUtcNow(), stoppingToken);
        }

        // One scope per draft: a conflict or failure on one draft never poisons another's DbContext.
        foreach (var draftId in expired)
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DraftExpiryService>()
                .CatchUpAsync(draftId, stoppingToken);
        }
    }
}
