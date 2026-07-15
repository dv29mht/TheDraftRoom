using System.Collections.Concurrent;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Datasets;
using FcDraft.Application.Features.Rosters;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Rosters;

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

/// <summary>Captures password-reset sends in memory so the reset seam can be unit-tested.</summary>
public sealed class RecordingPasswordResetEmailSender : IPasswordResetEmailSender
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

/// <summary>
/// Fast, deterministic password hasher for the unit suite so tests are not slowed by BCrypt's work
/// factor. The real <c>BCryptPasswordHasher</c> is exercised directly by its own test.
/// </summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "fake::" + password;

    public bool Verify(string hash, string password) => hash == "fake::" + password;
}

/// <summary>Records security-audit entries in memory so tests can assert on what was written.</summary>
public sealed class RecordingSecurityAuditService : ISecurityAuditService
{
    private readonly List<SecurityAuditEntry> _entries = [];

    public IReadOnlyList<SecurityAuditEntry> Entries => _entries;

    public Task RecordAsync(SecurityAuditEntry entry, CancellationToken cancellationToken)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SecurityAuditEvent>> GetRecentAsync(int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SecurityAuditEvent>>([]);
}

/// <summary>A settable clock for deterministic lockout-window tests.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

internal static class AsyncEnumerable
{
    public static async IAsyncEnumerable<T> Empty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}

/// <summary>
/// Serves the locked default 4-3-3 roster template so the draft handlers can be unit-tested without a
/// database. Construct with <c>hasActive: false</c> to exercise the "no active template" rejection path.
/// </summary>
public sealed class FakeRosterTemplateService(bool hasActive = true) : IRosterTemplateService
{
    public static readonly Guid TemplateId = new("00000000-0000-0000-0000-0000000000f1");

    private static RosterTemplateDetail Detail() => new(
        new RosterTemplateSummary(
            TemplateId, DefaultRosterTemplate.TemplateName, true, DefaultRosterTemplate.PickTimerSeconds,
            DefaultRosterTemplate.Slots().Count, DateTimeOffset.UnixEpoch),
        DefaultRosterTemplate.Slots()
            .Select(slot => new RosterSlotDto(slot.Order, slot.SlotType.ToString(), slot.Position, slot.Label))
            .ToArray());

    public Task<IReadOnlyList<RosterTemplateSummary>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RosterTemplateSummary>>([Detail().Summary]);

    public Task<RosterTemplateDetail?> GetAsync(Guid templateId, CancellationToken cancellationToken) =>
        Task.FromResult<RosterTemplateDetail?>(templateId == TemplateId ? Detail() : null);

    public Task<RosterTemplateDetail?> GetActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult<RosterTemplateDetail?>(hasActive ? Detail() : null);

    public Task<RosterTemplateSummary> CreateAsync(CreateRosterTemplateRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<RosterTemplateSummary> ActivateAsync(Guid templateId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>Reports a single Active dataset version so <c>StartDraftCommand</c> can pin one under test.</summary>
public sealed class FakeDatasetAdminService : IDatasetAdminService
{
    public static readonly Guid ActiveVersionId = new("00000000-0000-0000-0000-0000000000ab");

    public Task<IReadOnlyList<DatasetVersionSummary>> ListVersionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DatasetVersionSummary>>([
            new DatasetVersionSummary(
                ActiveVersionId, "Fake dataset", "test", "Active", 100, 10, 0, 0,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
        ]);

    public Task<DatasetImportReport> ImportAsync(DatasetImportRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<DatasetImportReport> ImportBundledAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<DatasetVersionDetail?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken) =>
        Task.FromResult<DatasetVersionDetail?>(null);

    public Task<DatasetVersionSummary> ActivateAsync(Guid versionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
