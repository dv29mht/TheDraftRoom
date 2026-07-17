using System.Collections.Concurrent;
using System.Security.Cryptography;
using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace FcDraft.Infrastructure.Auth;

public sealed class InMemoryIdentityService : IIdentityService
{
    private readonly ConcurrentDictionary<string, User> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PasswordResetToken> _resetTokens = new(StringComparer.Ordinal);
    private readonly IPasswordHasher _hasher;
    private readonly IEmailQueue _emailQueue;

    public InMemoryIdentityService(
        IEmailQueue emailQueue,
        IPasswordHasher hasher,
        IOptions<DatabaseOptions>? databaseOptions = null)
    {
        _emailQueue = emailQueue;
        _hasher = hasher;
        // mdevansh@gmail.com is the single designated administrator account (see PRD §9.2).
        AddDevelopmentUser("mdevansh@gmail.com", "Draft Room Admin", UserRole.Admin, "DraftAdmin@2026", mustChangePassword: false);
        AddDevelopmentUser("player@draftroom.dev", "Practice Player", UserRole.Player, "Player@2026", mustChangePassword: false);

        // The PR-23 demo players (2v2 needs 4+ activated accounts and Testing has no live email).
        if (databaseOptions?.Value.SeedDemoAccounts == true)
        {
            foreach (var demo in DemoAccounts.Players)
            {
                AddDevelopmentUser(demo.Email, demo.DisplayName, DemoAccounts.Role, demo.Password, mustChangePassword: false);
            }
        }
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _users.TryGetValue(Normalize(email), out var user);
        return Task.FromResult(user);
    }

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_users.Values.FirstOrDefault(user => user.Id == userId));
    }

    public Task<UserDirectoryPage> SearchUsersAsync(UserDirectoryQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pageSize = UserDirectory.NormalizePageSize(query.PageSize);

        var all = _users.Values;
        var matches = all
            .Where(user => Matches(user, query.Search))
            .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.Id)
            .ToArray();

        var totalPages = UserDirectory.TotalPages(matches.Length, pageSize);
        var page = Math.Clamp(query.Page < 1 ? 1 : query.Page, 1, totalPages);
        IReadOnlyList<User> items = matches
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return Task.FromResult(new UserDirectoryPage(
            items,
            page,
            pageSize,
            matches.Length,
            totalPages,
            all.Count(user => user.InvitationSentAt is not null),
            all.Count(user => !user.MustChangePassword)));
    }

    private static bool Matches(User user, string? search)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return true;
        }

        return user.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || user.Email.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<User> CreateUserAsync(
        string displayName,
        string email,
        UserRole role,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedEmail = Normalize(email);
        if (_users.ContainsKey(normalizedEmail))
        {
            throw new ConflictAppException("A user with this email address already exists.");
        }

        var temporaryPassword = CreateTemporaryPassword();
        var user = new User
        {
            DisplayName = displayName.Trim(),
            Email = email.Trim(),
            EmailNormalized = normalizedEmail,
            PasswordHash = string.Empty,
            Role = role,
            MustChangePassword = true,
        };
        user.PasswordHash = _hasher.Hash(temporaryPassword);
        if (!_users.TryAdd(normalizedEmail, user))
        {
            throw new ConflictAppException("A user with this email address already exists.");
        }

        try
        {
            await _emailQueue.EnqueueInvitationAsync(
                user.Email,
                user.DisplayName,
                temporaryPassword,
                cancellationToken);
            user.InvitationSentAt = DateTimeOffset.UtcNow;
            return user;
        }
        catch
        {
            // The in-memory foundation delivers inline, so a transport failure still rolls the
            // account back (there is no durable queue to retry from).
            _users.TryRemove(normalizedEmail, out _);
            throw;
        }
    }

    public Task<User> UpdateUserAsync(
        Guid userId,
        UserProfileUpdate update,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _users.Values.FirstOrDefault(candidate => candidate.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        var normalizedEmail = Normalize(update.Email);
        var emailChanged = !string.Equals(normalizedEmail, user.EmailNormalized, StringComparison.OrdinalIgnoreCase);
        if (emailChanged && _users.ContainsKey(normalizedEmail))
        {
            throw new ConflictAppException("A user with this email address already exists.");
        }

        var previousKey = user.EmailNormalized;
        user.DisplayName = update.DisplayName.Trim();
        user.Email = update.Email.Trim();
        user.EmailNormalized = normalizedEmail;
        user.Role = update.Role;
        user.AvatarUrl = UserDirectory.NullIfBlank(update.AvatarUrl);
        user.PreferredTeamName = UserDirectory.NullIfBlank(update.PreferredTeamName);

        if (emailChanged)
        {
            _users[normalizedEmail] = user;
            _users.TryRemove(previousKey, out _);
        }

        return Task.FromResult(user);
    }

    public Task<User> SetUserStatusAsync(Guid userId, AccountStatus status, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _users.Values.FirstOrDefault(candidate => candidate.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");
        user.Status = status;
        if (status == AccountStatus.Deactivated)
        {
            // Kill any token issued before deactivation immediately.
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }

        return Task.FromResult(user);
    }

    public async Task<User> SendInvitationAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _users.Values.FirstOrDefault(candidate => candidate.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");
        var temporaryPassword = CreateTemporaryPassword();
        await _emailQueue.EnqueueInvitationAsync(
            user.Email,
            user.DisplayName,
            temporaryPassword,
            cancellationToken);
        user.PasswordHash = _hasher.Hash(temporaryPassword);
        user.MustChangePassword = true;
        user.InvitationSentAt = DateTimeOffset.UtcNow;
        // A re-invite must invalidate any session established with the previous credential.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        return user;
    }

    public bool VerifyPassword(User user, string password) =>
        _hasher.Verify(user.PasswordHash, password);

    public Task ChangePasswordAsync(
        User user,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!VerifyPassword(user, currentPassword))
        {
            throw new UnauthorizedAppException("The current password is incorrect.");
        }

        ApplyNewPassword(user, newPassword);
        return Task.CompletedTask;
    }

    public Task<PasswordResetGrant?> CreatePasswordResetTokenAsync(string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _users.TryGetValue(Normalize(email), out var user);
        if (user is null || user.Status != AccountStatus.Active)
        {
            return Task.FromResult<PasswordResetGrant?>(null);
        }

        var token = ResetTokens.Generate();
        _resetTokens[ResetTokens.Hash(token)] = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = ResetTokens.Hash(token),
            ExpiresAt = DateTimeOffset.UtcNow.Add(ResetTokens.Lifetime),
        };
        return Task.FromResult<PasswordResetGrant?>(new PasswordResetGrant(user, token));
    }

    public Task<User> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hash = ResetTokens.Hash(token);
        if (!_resetTokens.TryGetValue(hash, out var grant) || !grant.IsUsable(DateTimeOffset.UtcNow))
        {
            throw new UnauthorizedAppException("This password reset link is invalid or has expired.");
        }

        var user = _users.Values.FirstOrDefault(candidate => candidate.Id == grant.UserId)
            ?? throw new UnauthorizedAppException("This password reset link is invalid or has expired.");

        grant.ConsumedAt = DateTimeOffset.UtcNow;
        ApplyNewPassword(user, newPassword);
        return Task.FromResult(user);
    }

    public Task<User> RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _users.Values.FirstOrDefault(candidate => candidate.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        return Task.FromResult(user);
    }

    public Task<User> SetOptionalEmailOptOutAsync(Guid userId, bool optOut, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _users.Values.FirstOrDefault(candidate => candidate.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");
        user.OptionalEmailOptOut = optOut;
        return Task.FromResult(user);
    }

    /// <summary>Sets a new password and rotates the security stamp so older tokens stop validating.</summary>
    private void ApplyNewPassword(User user, string newPassword)
    {
        user.PasswordHash = _hasher.Hash(newPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
    }

    private void AddDevelopmentUser(
        string email,
        string displayName,
        UserRole role,
        string password = "Draft@1234",
        bool mustChangePassword = true)
    {
        var user = new User
        {
            DisplayName = displayName,
            Email = email,
            EmailNormalized = Normalize(email),
            PasswordHash = string.Empty,
            Role = role,
            MustChangePassword = mustChangePassword
        };
        user.PasswordHash = _hasher.Hash(password);
        _users[user.EmailNormalized] = user;
    }

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();

    private static string CreateTemporaryPassword() =>
        $"Dr7!{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
}
