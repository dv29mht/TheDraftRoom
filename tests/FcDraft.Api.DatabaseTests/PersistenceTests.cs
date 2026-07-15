using System.Net;
using System.Net.Http.Json;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Proves the PR-03 definition of done against a real PostgreSQL server: the schema is created
/// exclusively from migrations, data survives an API restart, the database health check is wired
/// into <c>/health</c>, and the transaction abstraction commits and rolls back. Every test skips
/// cleanly when Docker is unavailable (see <see cref="PostgresFixture"/>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PersistenceTests(PostgresFixture fixture)
{
    [SkippableFact]
    public async Task Users_created_before_a_restart_are_still_present_afterward()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        const string email = "persisted.player@draftroom.test";

        // First "process": seed on startup, then create an invited user through the real API.
        await using (var first = new PostgresApiFactory(fixture.ConnectionString!))
        {
            var admin = await first.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
            var create = await first.CreateClient().WithBearer(admin.AccessToken)
                .PostAsJsonAsync("/api/users", new { email, displayName = "Durable Player" });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }

        // Second "process": a brand new host on the same database must still see the user.
        await using var second = new PostgresApiFactory(fixture.ConnectionString!);
        using var scope = second.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();

        var persisted = await identity.FindByEmailAsync(email, default);
        Assert.NotNull(persisted);
        Assert.True(persisted!.MustChangePassword);

        var seededAdmin = await identity.FindByEmailAsync(SeededAccounts.AdminEmail, default);
        Assert.NotNull(seededAdmin);
        Assert.Equal(UserRole.Admin, seededAdmin!.Role);
    }

    [SkippableFact]
    public async Task Login_succeeds_after_a_restart_using_the_persisted_password()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        const string email = "restart.login@draftroom.test";
        const string newPassword = "Persisted@2026Pass";

        await using (var first = new PostgresApiFactory(fixture.ConnectionString!))
        {
            var admin = await first.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
            await first.CreateClient().WithBearer(admin.AccessToken).PostAsJsonAsync("/api/users", new { email, displayName = "Durable Player" });

            var otp = first.EmailSender.PasswordFor(email);
            var firstLogin = await first.CreateClient().LoginAsync(email, otp);
            var change = await first.CreateClient().WithBearer(firstLogin.AccessToken)
                .PostAsJsonAsync("/api/auth/change-password", new
                {
                    currentPassword = otp,
                    newPassword,
                    confirmPassword = newPassword,
                });
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        }

        // The changed password must work against a fresh host reading from the same database.
        await using var second = new PostgresApiFactory(fixture.ConnectionString!);
        var login = await second.CreateClient().LoginAsync(email, newPassword);
        Assert.False(login.MustChangePassword);
    }

    [SkippableFact]
    public async Task Health_endpoint_reports_the_database_as_healthy_when_it_is_reachable()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("healthy", body!.Status);
        Assert.Equal("fc-draft-api", body.Service);
        Assert.Equal("healthy", body.Checks["database"]);
    }

    [SkippableFact]
    public async Task Schema_is_created_exclusively_from_migrations()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();

        var applied = await dbContext.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, migration => migration.EndsWith("InitialCreate"));

        var pending = await dbContext.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);

        // The platform metadata table exists and was seeded by the initializer.
        var platformName = await dbContext.PlatformMetadata
            .FirstOrDefaultAsync(metadata => metadata.Key == "platform.name");
        Assert.Equal("The Draft Room", platformName!.Value);
    }

    [SkippableFact]
    public async Task Transaction_runner_rolls_back_on_failure_and_commits_on_success()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        var rollbackKey = "test.rollback." + Guid.NewGuid().ToString("N");
        var commitKey = "test.commit." + Guid.NewGuid().ToString("N");

        // A failure inside the transaction leaves no row behind.
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var runner = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync(async ct =>
            {
                dbContext.PlatformMetadata.Add(new PlatformMetadata { Key = rollbackKey, Value = "rolled-back" });
                await dbContext.SaveChangesAsync(ct);
                throw new InvalidOperationException("Simulated failure after write.");
            }, default));
        }

        // A successful transaction persists.
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var runner = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();
            await runner.ExecuteAsync(async ct =>
            {
                dbContext.PlatformMetadata.Add(new PlatformMetadata { Key = commitKey, Value = "committed" });
                await dbContext.SaveChangesAsync(ct);
            }, default);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            Assert.False(await dbContext.PlatformMetadata.AnyAsync(metadata => metadata.Key == rollbackKey));
            Assert.True(await dbContext.PlatformMetadata.AnyAsync(metadata => metadata.Key == commitKey));
        }
    }
}
