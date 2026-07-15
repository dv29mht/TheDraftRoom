using FcDraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Verifies the database health check reports Unhealthy when the server cannot be reached. This
/// needs no container — it points at a closed port — so it runs everywhere, including machines
/// without Docker.
/// </summary>
public sealed class DatabaseHealthCheckTests
{
    [Fact]
    public async Task Reports_unhealthy_when_the_database_is_unreachable()
    {
        var options = new DbContextOptionsBuilder<FcDraftDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=draftroom;Username=none;Password=none;Timeout=1;Command Timeout=1;")
            .Options;
        await using var dbContext = new FcDraftDbContext(options);
        var check = new DatabaseHealthCheck(dbContext);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
