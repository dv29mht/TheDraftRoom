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

/// <summary>Captures reset tokens in memory so the reset flow can be driven without real email.</summary>
public sealed class FakePasswordResetEmailSender : IPasswordResetEmailSender
{
    private readonly ConcurrentDictionary<string, string> _tokensByEmail =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetToken(string email, out string token) => _tokensByEmail.TryGetValue(email, out token!);

    public Task SendAsync(string email, string displayName, string resetToken, CancellationToken cancellationToken)
    {
        _tokensByEmail[email] = resetToken;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Boots the real API in-process with the live in-memory identity store (deterministic seeded
/// accounts) but swaps the Brevo sender for <see cref="FakeInvitationEmailSender"/>. Each test
/// class gets its own factory instance, so the in-memory directory is isolated per class.
/// </summary>
public class DraftRoomApiFactory : WebApplicationFactory<Program>
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

            services.RemoveAll<IPasswordResetEmailSender>();
            services.AddSingleton<FakePasswordResetEmailSender>();
            services.AddSingleton<IPasswordResetEmailSender>(sp => sp.GetRequiredService<FakePasswordResetEmailSender>());
        });
    }

    public FakeInvitationEmailSender EmailSender => Services.GetRequiredService<FakeInvitationEmailSender>();

    public FakePasswordResetEmailSender ResetEmailSender => Services.GetRequiredService<FakePasswordResetEmailSender>();

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

/// <summary>
/// A settable clock for driving the PR-16 turn timer hermetically. Starts at the real "now" so issued
/// JWTs stay valid, then advances only when a test moves it.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

/// <summary>
/// The in-memory API host with the whole app running on a settable <see cref="TestClock"/> (the
/// registered <see cref="TimeProvider"/> seam), so timer tests advance time instead of sleeping. The
/// hosted expiry sweep reads the same clock, keeping the suite deterministic.
/// </summary>
public class TimedApiFactory : DraftRoomApiFactory
{
    public TestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}
