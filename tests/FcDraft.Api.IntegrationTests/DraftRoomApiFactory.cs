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
/// Captures draft-lifecycle emails (PR-20). <see cref="FailuresRemaining"/> simulates a Brevo outage:
/// the next N sends throw — the direct queue must swallow them so no draft mutation ever fails on mail.
/// </summary>
public sealed class FakeDraftEmailSender : IDraftEmailSender
{
    public sealed record CapturedDraftEmail(string Template, string Email, DraftEmailPayload Payload);

    private readonly List<CapturedDraftEmail> _sent = [];
    private int _failuresRemaining;

    public IReadOnlyList<CapturedDraftEmail> Sent { get { lock (_sent) { return _sent.ToArray(); } } }

    public int FailuresRemaining { get => _failuresRemaining; set => _failuresRemaining = value; }

    public Task SendInvitationAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken) =>
        RecordAsync("invitation", email, payload);

    public Task SendReminderAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken) =>
        RecordAsync("reminder", email, payload);

    public Task SendCancelledAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken) =>
        RecordAsync("cancelled", email, payload);

    public Task SendCompletedAsync(string email, string displayName, DraftEmailPayload payload, CancellationToken cancellationToken) =>
        RecordAsync("completed", email, payload);

    private Task RecordAsync(string template, string email, DraftEmailPayload payload)
    {
        if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
        {
            throw new InvalidOperationException("Simulated Brevo outage.");
        }

        lock (_sent)
        {
            _sent.Add(new CapturedDraftEmail(template, email, payload));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Captures announcement emails (PR-21). <see cref="FailuresRemaining"/> simulates a Brevo outage:
/// the next N sends throw — the direct queue must swallow them so a confirmed announcement never
/// fails on mail.
/// </summary>
public sealed class FakeAnnouncementEmailSender : IAnnouncementEmailSender
{
    public sealed record CapturedAnnouncementEmail(string Email, AnnouncementEmailPayload Payload);

    private readonly List<CapturedAnnouncementEmail> _sent = [];
    private int _failuresRemaining;

    public IReadOnlyList<CapturedAnnouncementEmail> Sent { get { lock (_sent) { return _sent.ToArray(); } } }

    public int FailuresRemaining { get => _failuresRemaining; set => _failuresRemaining = value; }

    public Task SendAsync(string email, string displayName, AnnouncementEmailPayload payload, CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
        {
            throw new InvalidOperationException("Simulated Brevo outage.");
        }

        lock (_sent)
        {
            _sent.Add(new CapturedAnnouncementEmail(email, payload));
        }

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

            services.RemoveAll<IDraftEmailSender>();
            services.AddSingleton<FakeDraftEmailSender>();
            services.AddSingleton<IDraftEmailSender>(sp => sp.GetRequiredService<FakeDraftEmailSender>());

            services.RemoveAll<IAnnouncementEmailSender>();
            services.AddSingleton<FakeAnnouncementEmailSender>();
            services.AddSingleton<IAnnouncementEmailSender>(sp => sp.GetRequiredService<FakeAnnouncementEmailSender>());
        });
    }

    public FakeInvitationEmailSender EmailSender => Services.GetRequiredService<FakeInvitationEmailSender>();

    public FakePasswordResetEmailSender ResetEmailSender => Services.GetRequiredService<FakePasswordResetEmailSender>();

    public FakeDraftEmailSender DraftEmailSender => Services.GetRequiredService<FakeDraftEmailSender>();

    public FakeAnnouncementEmailSender AnnouncementEmailSender => Services.GetRequiredService<FakeAnnouncementEmailSender>();

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
