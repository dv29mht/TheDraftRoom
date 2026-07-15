using System.Collections.Concurrent;
using FcDraft.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FcDraft.Api.DatabaseTests;

/// <summary>Captures invitation sends in memory so the tests never call Brevo.</summary>
public sealed class CapturingInvitationEmailSender : IInvitationEmailSender
{
    private readonly ConcurrentDictionary<string, string> _passwordsByEmail =
        new(StringComparer.OrdinalIgnoreCase);

    public string PasswordFor(string email) => _passwordsByEmail[email];

    public Task SendAsync(string email, string displayName, string temporaryPassword, CancellationToken cancellationToken)
    {
        _passwordsByEmail[email] = temporaryPassword;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Boots the real API against a live PostgreSQL connection string. Each instance is a full host, so
/// disposing one and creating another that points at the same database models an API restart — the
/// mechanism the restart-persistence test relies on. The Brevo sender is faked so the invite flow
/// can be driven without sending real email.
/// </summary>
public sealed class PostgresApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public CapturingInvitationEmailSender EmailSender => Services.GetRequiredService<CapturingInvitationEmailSender>();

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
