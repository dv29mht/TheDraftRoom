using System.Collections.Concurrent;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;

namespace FcDraft.UnitTests;

/// <summary>
/// Deterministic identities used across the unit suite. These mirror the seeded
/// Development accounts so tests never depend on random data or a live directory.
/// </summary>
public static class TestIdentities
{
    public const string AdminEmail = "admin@draftroom.test";
    public const string AdminPassword = "Admin@2026Secure";
    public const string PlayerEmail = "player@draftroom.test";
    public const string PlayerPassword = "Player@2026Secure";
}

/// <summary>
/// Captures invitation sends in memory instead of calling Brevo. The captured
/// one-time password lets tests exercise the full invite -> first-login flow.
/// </summary>
public sealed class RecordingInvitationEmailSender : IInvitationEmailSender
{
    private readonly ConcurrentDictionary<string, string> _passwordsByEmail =
        new(StringComparer.OrdinalIgnoreCase);

    public bool ShouldThrow { get; set; }
    public int SendCount { get; private set; }

    public IReadOnlyDictionary<string, string> PasswordsByEmail => _passwordsByEmail;

    public string PasswordFor(string email) => _passwordsByEmail[email];

    public Task SendAsync(string email, string displayName, string temporaryPassword, CancellationToken cancellationToken)
    {
        if (ShouldThrow)
        {
            throw new InvalidOperationException("Simulated email transport failure.");
        }

        SendCount++;
        _passwordsByEmail[email] = temporaryPassword;
        return Task.CompletedTask;
    }
}

/// <summary>Issues a deterministic, opaque token so handlers can be unit-tested without JWT config.</summary>
public sealed class FakeTokenService : ITokenService
{
    public static readonly DateTimeOffset FixedExpiry = new(2026, 07, 14, 12, 00, 00, TimeSpan.Zero);

    public TokenResult Create(User user) =>
        new($"test-token::{user.Email}::{user.Role.ToString().ToLowerInvariant()}", FixedExpiry);
}

/// <summary>Records published admin notifications without a live channel.</summary>
public sealed class RecordingAdminNotificationService : IAdminNotificationService
{
    private readonly List<AdminNotification> _published = [];

    public IReadOnlyList<AdminNotification> Published => _published;

    public IReadOnlyCollection<AdminNotification> Recent() => _published.AsReadOnly();

    public void Publish(string type, string title, string message) =>
        _published.Add(new AdminNotification(Guid.NewGuid(), type, title, message, FakeTokenService.FixedExpiry));

    public IAsyncEnumerable<AdminNotification> SubscribeAsync(CancellationToken cancellationToken) =>
        AsyncEnumerable.Empty<AdminNotification>();
}

internal static class AsyncEnumerable
{
    public static async IAsyncEnumerable<T> Empty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
