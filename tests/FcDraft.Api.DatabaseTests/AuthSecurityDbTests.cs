using System.Net;
using System.Net.Http.Json;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Proves the PR-05 security boundary against a real PostgreSQL server: security-audit events and
/// password-reset grants persist, and a rotated security stamp (sign-out-everywhere) survives an API
/// restart so previously issued tokens stay revoked. Skips cleanly when Docker is unavailable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthSecurityDbTests(PostgresFixture fixture)
{
    /// <summary>Invites and activates an account with a known password; returns its id and password.</summary>
    private static async Task<(Guid id, string password)> CreateActiveUserAsync(
        PostgresApiFactory factory, string email)
    {
        var admin = await factory.CreateClient().LoginAsync(SeededAccounts.AdminEmail, SeededAccounts.AdminPassword);
        var create = await factory.CreateClient().WithBearer(admin.AccessToken)
            .PostAsJsonAsync("/api/users", new { email, displayName = "DB Security Tester" });
        var created = (await create.Content.ReadFromJsonAsync<UserSummary>())!;

        var otp = factory.EmailSender.PasswordFor(email);
        var firstLogin = await factory.CreateClient().LoginAsync(email, otp);
        const string password = "DbSecure@2026Pass";
        await factory.CreateClient().WithBearer(firstLogin.AccessToken).PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = otp,
            newPassword = password,
            confirmPassword = password
        });
        return (created.Id, password);
    }

    [SkippableFact]
    public async Task Sign_in_and_password_change_write_persisted_audit_events()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        var email = $"audit-{Guid.NewGuid():N}@draftroom.test";
        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        await CreateActiveUserAsync(factory, email); // performs a sign-in and a password change

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
        var normalized = email.ToUpperInvariant();

        var events = await dbContext.SecurityAuditEvents
            .Where(audit => audit.Email == email || audit.Email == normalized)
            .ToListAsync();

        Assert.Contains(events, audit => audit.Action == SecurityAuditAction.PasswordChanged);
    }

    [SkippableFact]
    public async Task Password_reset_grant_persists_and_is_consumed_once_used()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        var email = $"dbreset-{Guid.NewGuid():N}@draftroom.test";
        await using var factory = new PostgresApiFactory(fixture.ConnectionString!);
        var (userId, _) = await CreateActiveUserAsync(factory, email);

        var forgot = await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        var token = factory.ResetEmailSender.TokenFor(email);

        const string newPassword = "DbReset@2026Fresh";
        var reset = await factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password", new
        {
            token,
            newPassword,
            confirmPassword = newPassword
        });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FcDraftDbContext>();
        var grant = await dbContext.PasswordResetTokens.SingleAsync(row => row.UserId == userId);
        Assert.NotNull(grant.ConsumedAt);

        // The new password works against the persisted store.
        var login = await factory.CreateClient().LoginAsync(email, newPassword);
        Assert.False(login.MustChangePassword);
    }

    [SkippableFact]
    public async Task Sign_out_everywhere_keeps_tokens_revoked_across_a_restart()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        var email = $"dbrevoke-{Guid.NewGuid():N}@draftroom.test";
        string staleToken;

        await using (var first = new PostgresApiFactory(fixture.ConnectionString!))
        {
            var (_, password) = await CreateActiveUserAsync(first, email);
            var session = await first.CreateClient().LoginAsync(email, password);
            staleToken = session.AccessToken;

            var revoke = await first.CreateClient().WithBearer(staleToken).PostAsync("/api/auth/logout-all", null);
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        // A brand new host on the same database must still reject the pre-revocation token, proving
        // the rotated security stamp was persisted.
        await using var second = new PostgresApiFactory(fixture.ConnectionString!);
        var afterRestart = await second.CreateClient().WithBearer(staleToken).GetAsync("/api/drafts");
        Assert.Equal(HttpStatusCode.Unauthorized, afterRestart.StatusCode);
    }
}
