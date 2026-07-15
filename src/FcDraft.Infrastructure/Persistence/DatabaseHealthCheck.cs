using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FcDraft.Infrastructure.Persistence;

/// <summary>
/// Reports whether the PostgreSQL database backing the application is reachable. Wired into the
/// <c>/health</c> endpoint so operators can distinguish a live database from an unreachable one.
/// </summary>
public sealed class DatabaseHealthCheck(FcDraftDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("The database connection is available.")
                : HealthCheckResult.Unhealthy("The database is unreachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("The database connectivity check failed.", exception);
        }
    }
}
