using System.Collections.Concurrent;
using FcDraft.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FcDraft.Api.IntegrationTests;

/// <summary>
/// Records invitation sends in memory so integration tests never call Brevo. The captured
/// one-time password is what lets a test drive the real invite -> first-login -> change flow.
/// </summary>
public sealed class FakeInvitationEmailSender : IInvitationEmailSender
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
/// Boots the real API in-process with the live in-memory identity store (deterministic seeded
/// accounts) but swaps the Brevo sender for <see cref="FakeInvitationEmailSender"/>. Each test
/// class gets its own factory instance, so the in-memory directory is isolated per class.
/// </summary>
public sealed class DraftRoomApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Point the host at the API project directory so appsettings.json (which holds the
        // development JWT signing key) resolves regardless of where the test binaries run.
        builder.UseContentRoot(LocateApiContentRoot());

        // "Testing" avoids loading the developer's gitignored appsettings.Development.json,
        // keeping the suite hermetic and independent of any local Brevo secret.
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInvitationEmailSender>();
            services.AddSingleton<FakeInvitationEmailSender>();
            services.AddSingleton<IInvitationEmailSender>(sp => sp.GetRequiredService<FakeInvitationEmailSender>());
        });
    }

    public FakeInvitationEmailSender EmailSender => Services.GetRequiredService<FakeInvitationEmailSender>();

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
