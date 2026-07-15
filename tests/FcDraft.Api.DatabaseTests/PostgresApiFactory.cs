using System.Collections.Concurrent;
using System.Linq;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Infrastructure.Email;
using FcDraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FcDraft.Api.DatabaseTests;

/// <summary>
/// Captures invitation sends in memory so the tests never call Brevo. <see cref="FailuresRemaining"/>
/// lets the outbox tests simulate a transient Brevo outage: while positive, each send throws and
/// decrements, so the durable outbox's retry can be exercised deterministically.
/// </summary>
public sealed class CapturingInvitationEmailSender : IInvitationEmailSender
{
    private readonly ConcurrentDictionary<string, string> _passwordsByEmail =
        new(StringComparer.OrdinalIgnoreCase);

    public int FailuresRemaining { get; set; }

    public bool TryGetPassword(string email, out string password) => _passwordsByEmail.TryGetValue(email, out password!);

    public string PasswordFor(string email) => _passwordsByEmail[email];

    public Task SendAsync(string email, string displayName, string temporaryPassword, CancellationToken cancellationToken)
    {
        if (FailuresRemaining > 0)
        {
            FailuresRemaining--;
            throw new InvalidOperationException("Simulated Brevo outage.");
        }

        _passwordsByEmail[email] = temporaryPassword;
        return Task.CompletedTask;
    }
}

/// <summary>Captures reset tokens in memory so the reset flow can be driven without real email.</summary>
public sealed class CapturingPasswordResetEmailSender : IPasswordResetEmailSender
{
    private readonly ConcurrentDictionary<string, string> _tokensByEmail =
        new(StringComparer.OrdinalIgnoreCase);

    public string TokenFor(string email) => _tokensByEmail[email];

    public Task SendAsync(string email, string displayName, string resetToken, CancellationToken cancellationToken)
    {
        _tokensByEmail[email] = resetToken;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Boots the real API against a live PostgreSQL connection string. Each instance is a full host, so
/// disposing one and creating another that points at the same database models an API restart — the
/// mechanism the restart-persistence test relies on. The Brevo sender is faked so the invite flow
/// can be driven without sending real email.
/// </summary>
public sealed class PostgresApiFactory(string connectionString, bool useEmailOutbox = false)
    : WebApplicationFactory<Program>
{
    public CapturingInvitationEmailSender EmailSender => Services.GetRequiredService<CapturingInvitationEmailSender>();

    public CapturingPasswordResetEmailSender ResetEmailSender => Services.GetRequiredService<CapturingPasswordResetEmailSender>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(LocateApiContentRoot());
        builder.UseEnvironment("Testing");

        // UseSetting is applied to the host configuration immediately, so these values are visible
        // when Program calls AddInfrastructure(builder.Configuration). ConfigureAppConfiguration
        // would run at builder.Build() — after the connection string has already been read — leaving
        // the app in in-memory mode.
        builder.UseSetting("ConnectionStrings:DraftRoom", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:SeedDevelopmentAccounts", "true");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInvitationEmailSender>();
            services.AddSingleton<CapturingInvitationEmailSender>();
            services.AddSingleton<IInvitationEmailSender>(sp => sp.GetRequiredService<CapturingInvitationEmailSender>());

            services.RemoveAll<IPasswordResetEmailSender>();
            services.AddSingleton<CapturingPasswordResetEmailSender>();
            services.AddSingleton<IPasswordResetEmailSender>(sp => sp.GetRequiredService<CapturingPasswordResetEmailSender>());

            // Remove the background delivery loop so tests drive the outbox deterministically through
            // IEmailOutboxProcessor instead of racing a timer.
            var worker = services.FirstOrDefault(descriptor => descriptor.ImplementationType == typeof(EmailOutboxWorker));
            if (worker is not null)
            {
                services.Remove(worker);
            }

            // By default deliver inline so most DB tests can read the captured one-time password
            // immediately. The dedicated outbox tests pass useEmailOutbox: true to exercise the real
            // durable queue + processor instead.
            if (!useEmailOutbox)
            {
                services.RemoveAll<IEmailQueue>();
                services.AddScoped<IEmailQueue, DirectEmailQueue>();
            }
        });
    }

    private static string LocateApiContentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "FcDraft.API");
            if (File.Exists(Path.Combine(candidate, "FcDraft.API.csproj")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the FcDraft.API project directory from the test output.");
    }
}
