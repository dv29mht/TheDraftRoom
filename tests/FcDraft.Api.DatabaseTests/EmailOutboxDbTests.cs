using System.Net;
using System.Net.Http.Json;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Proves the PR-06 durable email outbox against a real PostgreSQL server: account creation commits
/// even while Brevo is unavailable, the queued work retries safely until delivered, the stored secret
/// is cleared once sent, and delivery status is observable to an admin without exposing that secret.
/// The background worker is removed in the test host so delivery is driven deterministically through
/// <see cref="IEmailOutboxProcessor"/>. Skips cleanly when Docker is unavailable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EmailOutboxDbTests(PostgresFixture fixture)
{
    private sealed record OutboxRow(
        Guid Id,
        string Kind,
        string ToEmail,
        string Status,
        int AttemptCount,
        string? LastError,
        DateTimeOffset CreatedAt,
        DateTimeOffset? SentAt,
        DateTimeOffset NextAttemptAt);

    private static async Task<HttpClient> AdminAsync(PostgresApiFactory factory)
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        return factory.CreateClient().WithBearer(admin.AccessToken);
    }

    private static async Task ProcessOutboxAsync(PostgresApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IEmailOutboxProcessor>();
        await processor.ProcessDueAsync(default);
    }

    [SkippableFact]
    public async Task User_creation_commits_during_a_brevo_outage_and_delivers_on_retry()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        var email = $"outbox-{Guid.NewGuid():N}@draftroom.test";
        await using var factory = new PostgresApiFactory(fixture.ConnectionString!, useEmailOutbox: true);
        var admin = await AdminAsync(factory);

        // Brevo is "down" for the first delivery attempt.
        factory.EmailSender.FailuresRemaining = 1;

        // Account creation still succeeds — the email is only queued, not sent inline.
        var create = await admin.PostAsJsonAsync("/api/users", new { email, displayName = "Outbox Player" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // First delivery attempt fails; the row stays pending with a recorded attempt and error.
        await ProcessOutboxAsync(factory);
        var afterFailure = (await admin.GetFromJsonAsync<List<OutboxRow>>("/api/admin/email-outbox"))!
            .Single(row => row.ToEmail == email);
        Assert.Equal("Pending", afterFailure.Status);
        Assert.Equal(1, afterFailure.AttemptCount);
        Assert.NotNull(afterFailure.LastError);

        // The account is retained regardless of the mail failure.
        var directory = await admin.GetFromJsonAsync<PagedUsersLite>($"/api/users?search={Uri.EscapeDataString(email)}");
        Assert.Equal(1, directory!.Total);

        // Bring the retry forward (past its backoff) and process again; Brevo now succeeds.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var message = await db.EmailOutbox.SingleAsync(row => row.ToEmail == email);
            message.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        await ProcessOutboxAsync(factory);

        var delivered = (await admin.GetFromJsonAsync<List<OutboxRow>>("/api/admin/email-outbox"))!
            .Single(row => row.ToEmail == email);
        Assert.Equal("Sent", delivered.Status);
        Assert.NotNull(delivered.SentAt);

        // The secret is cleared from the row once delivered, and the delivered one-time password works.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
            var message = await db.EmailOutbox.SingleAsync(row => row.ToEmail == email);
            Assert.Null(message.Secret);
        }

        var otp = factory.EmailSender.PasswordFor(email);
        var login = await factory.CreateClient().LoginAsync(email, otp);
        Assert.True(login.MustChangePassword);
    }

    [SkippableFact]
    public async Task Outbox_status_endpoint_never_exposes_the_secret()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        var email = $"outbox-secret-{Guid.NewGuid():N}@draftroom.test";
        await using var factory = new PostgresApiFactory(fixture.ConnectionString!, useEmailOutbox: true);
        var admin = await AdminAsync(factory);

        await admin.PostAsJsonAsync("/api/users", new { email, displayName = "Secret Player" });
        await ProcessOutboxAsync(factory);

        var otp = factory.EmailSender.PasswordFor(email);
        var raw = await admin.GetStringAsync("/api/admin/email-outbox");

        Assert.Contains(email, raw);
        Assert.Contains("Sent", raw);
        // The one-time password must never appear in the observability payload.
        Assert.DoesNotContain(otp, raw);
    }

    private sealed record PagedUsersLite(int Total);
}
