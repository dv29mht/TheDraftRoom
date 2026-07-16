using System.Collections.Concurrent;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Datasets;
using FcDraft.Application.Features.Drafts;
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

/// <summary>
/// A deterministic <see cref="IShuffler"/> for the spinner tests: it reverses the list in place, a fixed
/// non-identity permutation, so a test can assert the committed order came from the injected seam (and not
/// from insertion order) and is reproducible.
/// </summary>
public sealed class ReversingShuffler : IShuffler
{
    public void Shuffle<T>(IList<T> items)
    {
        for (int low = 0, high = items.Count - 1; low < high; low++, high--)
        {
            (items[low], items[high]) = (items[high], items[low]);
        }
    }
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

/// <summary>
/// A minimal in-memory directory for unit-testing the lobby handlers: add accounts, look them up by id or
/// email, and page through them for the invitable-users query. Mutating account operations are out of scope
/// and throw. Use <see cref="Add"/> to seed hosts/invitees and to model a deactivated account.
/// </summary>
public sealed class FakeIdentityDirectory : IIdentityService
{
    private readonly List<User> _users = [];

    public User Add(string displayName, AccountStatus status = AccountStatus.Active, UserRole role = UserRole.Player)
    {
        var email = $"{displayName.Replace(" ", "").ToLowerInvariant()}.{_users.Count}@draftroom.test";
        var user = new User
        {
            DisplayName = displayName,
            Email = email,
            EmailNormalized = email.ToUpperInvariant(),
            PasswordHash = "fake",
            Role = role,
            Status = status,
            MustChangePassword = false,
        };
        _users.Add(user);
        return user;
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(_users.FirstOrDefault(user =>
            string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)));

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.FirstOrDefault(user => user.Id == userId));

    public Task<UserDirectoryPage> SearchUsersAsync(UserDirectoryQuery query, CancellationToken cancellationToken)
    {
        var term = query.Search?.Trim();
        var matches = _users
            .Where(user => string.IsNullOrEmpty(term)
                || user.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || user.Email.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var pageSize = query.PageSize < 1 ? 10 : query.PageSize;
        var totalPages = Math.Max(1, (int)Math.Ceiling(matches.Length / (double)pageSize));
        var page = Math.Clamp(query.Page < 1 ? 1 : query.Page, 1, totalPages);
        IReadOnlyList<User> items = matches.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return Task.FromResult(new UserDirectoryPage(
            items, page, pageSize, matches.Length, totalPages,
            matches.Count(user => user.InvitationSentAt is not null),
            matches.Count(user => !user.MustChangePassword)));
    }

    public Task<User> SetUserStatusAsync(Guid userId, AccountStatus status, CancellationToken cancellationToken)
    {
        var user = _users.First(candidate => candidate.Id == userId);
        user.Status = status;
        return Task.FromResult(user);
    }

    public Task<User> CreateUserAsync(string displayName, string email, UserRole role, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<User> UpdateUserAsync(Guid userId, UserProfileUpdate update, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<User> SendInvitationAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public bool VerifyPassword(User user, string password) => throw new NotSupportedException();

    public Task ChangePasswordAsync(User user, string currentPassword, string newPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PasswordResetGrant?> CreatePasswordResetTokenAsync(string email, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<User> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<User> RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
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

/// <summary>
/// A seedable draft eligibility catalog for unit-testing the PR-14/PR-15 handlers without a dataset. Add
/// eligible five-star clubs and footballers, then the handlers see the same club/held/position pools the
/// production catalog would surface. <see cref="SeedStandardLeague"/> creates a pool large enough to drive a
/// full position draft to completion.
/// </summary>
public sealed class FakeDraftCatalog : IDraftCatalog
{
    private readonly List<CatalogClub> _clubs = [];
    private readonly List<CatalogFootballer> _footballers = [];

    public CatalogClub AddClub(string name, string league = "Test League")
    {
        var club = new CatalogClub(Guid.NewGuid(), name, league);
        _clubs.Add(club);
        return club;
    }

    public CatalogFootballer AddFootballer(int id, string name, int overall, CatalogClub club, params string[] positions)
    {
        var footballer = new CatalogFootballer(id, name, overall, club.Id, club.Name, positions.Select(p => p.ToUpperInvariant()).ToArray());
        _footballers.Add(footballer);
        return footballer;
    }

    /// <summary>Seeds <paramref name="clubCount"/> five-star clubs, each with several 75+ players in every position, so any small draft can complete.</summary>
    public IReadOnlyList<CatalogClub> SeedStandardLeague(int clubCount = 3)
    {
        var positions = new[] { "ST", "LW", "RW", "CM", "LB", "CB", "RB", "GK" };
        var names = new[] { "Real Madrid", "FC Barcelona", "Manchester City", "Liverpool", "Arsenal", "Chelsea" };
        var clubs = new List<CatalogClub>();
        var id = 1000;
        for (var index = 0; index < clubCount; index++)
        {
            var club = AddClub(names[index % names.Length] + (index >= names.Length ? $" {index}" : string.Empty));
            clubs.Add(club);
            foreach (var position in positions)
            {
                for (var copy = 0; copy < 5; copy++)
                {
                    AddFootballer(id++, $"{club.Name} {position}{copy}", 88 - copy, club, position);
                }
            }
        }

        return clubs;
    }

    public Task<IReadOnlyList<CatalogClub>> ListFiveStarClubsAsync(Guid? datasetVersionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CatalogClub>>(_clubs.OrderBy(club => club.Name).ToArray());

    public Task<CatalogClub?> FindFiveStarClubAsync(Guid? datasetVersionId, Guid clubId, CancellationToken cancellationToken) =>
        Task.FromResult(_clubs.FirstOrDefault(club => club.Id == clubId));

    public Task<CatalogFootballer?> FindFootballerAsync(Guid? datasetVersionId, int footballerId, CancellationToken cancellationToken) =>
        Task.FromResult(_footballers.FirstOrDefault(footballer => footballer.Id == footballerId));

    public Task<IReadOnlyList<CatalogFootballer>> ListFootballersAsync(
        Guid? datasetVersionId, CatalogFootballerFilter filter, CancellationToken cancellationToken)
    {
        var query = _footballers.AsEnumerable();
        if (filter.ClubId is { } clubId)
        {
            query = query.Where(footballer => footballer.ClubId == clubId);
        }
        if (!string.IsNullOrWhiteSpace(filter.Position))
        {
            query = query.Where(footballer => footballer.Positions.Any(position => string.Equals(position, filter.Position, StringComparison.OrdinalIgnoreCase)));
        }

        // Matches the production catalogs' ordering — highest overall → name → stable id — which is also
        // the DRAFT_RULES auto-pick tie-break, so the PR-16 expiry tests exercise the real selection rule.
        IReadOnlyList<CatalogFootballer> results = query
            .OrderByDescending(footballer => footballer.Overall)
            .ThenBy(footballer => footballer.Name, StringComparer.Ordinal)
            .ThenBy(footballer => footballer.Id)
            .Take(Math.Clamp(filter.Take, 1, 500))
            .ToArray();
        return Task.FromResult(results);
    }
}
