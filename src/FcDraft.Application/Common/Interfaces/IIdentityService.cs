using FcDraft.Domain.Entities;

namespace FcDraft.Application.Common.Interfaces;

public interface IIdentityService
{
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken);
    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns one page of the directory. Filtering, counting, and paging execute in the store
    /// (the database when persistence is configured) so the whole directory is never loaded into
    /// memory. The invited/activated tallies are directory-wide, independent of the search filter.
    /// </summary>
    Task<UserDirectoryPage> SearchUsersAsync(UserDirectoryQuery query, CancellationToken cancellationToken);

    Task<User> CreateUserAsync(string displayName, string email, UserRole role, CancellationToken cancellationToken);
    Task<User> UpdateUserAsync(Guid userId, UserProfileUpdate update, CancellationToken cancellationToken);
    Task<User> SetUserStatusAsync(Guid userId, AccountStatus status, CancellationToken cancellationToken);
    Task<User> SendInvitationAsync(Guid userId, CancellationToken cancellationToken);
    bool VerifyPassword(User user, string password);
    Task ChangePasswordAsync(User user, string currentPassword, string newPassword, CancellationToken cancellationToken);

    /// <summary>
    /// Issues a single-use reset grant for an active account and returns the plaintext token to email
    /// (only the hash is stored). Returns null when no active account matches, so the caller can
    /// respond identically either way and avoid leaking which emails exist.
    /// </summary>
    Task<PasswordResetGrant?> CreatePasswordResetTokenAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Consumes a valid reset token: sets the new password, clears the must-change flag, and rotates
    /// the security stamp (revoking every older session). Throws when the token is unknown, expired,
    /// or already used.
    /// </summary>
    Task<User> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken);

    /// <summary>Rotates the security stamp so every existing token for the account stops validating.</summary>
    Task<User> RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>A freshly minted reset grant: the account plus the plaintext token to email it.</summary>
public sealed record PasswordResetGrant(User User, string Token);

/// <summary>Search/paging inputs for the admin user directory. Normalization happens in the service.</summary>
public sealed record UserDirectoryQuery(string? Search, int Page, int PageSize);

/// <summary>Editable account fields. Avatar and preferred team name are optional (null clears them).</summary>
public sealed record UserProfileUpdate(
    string DisplayName,
    string Email,
    UserRole Role,
    string? AvatarUrl,
    string? PreferredTeamName);

/// <summary>One page of the directory plus directory-wide health tallies.</summary>
public sealed record UserDirectoryPage(
    IReadOnlyList<User> Items,
    int Page,
    int PageSize,
    int Total,
    int TotalPages,
    int InvitedCount,
    int ActivatedCount);

public interface ITokenService
{
    TokenResult Create(User user);
}

public sealed record TokenResult(string AccessToken, DateTimeOffset ExpiresAt);
