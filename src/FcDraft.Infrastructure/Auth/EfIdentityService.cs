using System.Security.Cryptography;
using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FcDraft.Infrastructure.Auth;

/// <summary>
/// PostgreSQL-backed identity service. Preserves the behavior of the in-memory foundation (invite
/// with a one-time password, deactivation, forced first-login change) while persisting the user
/// directory so it survives an API restart. Per PR-04 the directory is durable: search, counting,
/// and paging run in the database; accounts are deactivated and retained rather than hard-deleted;
/// and optional avatar/preferred-team-name profile fields persist.
/// </summary>
public sealed class EfIdentityService(
    FcDraftDbContext dbContext,
    IEmailQueue emailQueue,
    IPasswordHasher hasher)
    : IIdentityService
{
    private readonly IPasswordHasher _hasher = hasher;

    public async Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = Normalize(email);
        return await dbContext.Users
            .FirstOrDefaultAsync(user => user.EmailNormalized == normalized, cancellationToken);
    }

    public async Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<UserDirectoryPage> SearchUsersAsync(
        UserDirectoryQuery query,
        CancellationToken cancellationToken)
    {
        var pageSize = UserDirectory.NormalizePageSize(query.PageSize);

        var filtered = dbContext.Users.AsNoTracking();
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // ILIKE keeps the match case-insensitive in PostgreSQL; the wildcard metacharacters in
            // the user's term are escaped so a literal % or _ cannot widen the search.
            var pattern = $"%{UserDirectory.EscapeLike(search)}%";
            filtered = filtered.Where(user =>
                EF.Functions.ILike(user.DisplayName, pattern, UserDirectory.LikeEscape)
                || EF.Functions.ILike(user.Email, pattern, UserDirectory.LikeEscape));
        }

        var total = await filtered.CountAsync(cancellationToken);
        var totalPages = UserDirectory.TotalPages(total, pageSize);
        var page = Math.Clamp(query.Page < 1 ? 1 : query.Page, 1, totalPages);

        var items = await filtered
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        // Directory-wide tallies, independent of the search filter.
        var invitedCount = await dbContext.Users.CountAsync(user => user.InvitationSentAt != null, cancellationToken);
        var activatedCount = await dbContext.Users.CountAsync(user => !user.MustChangePassword, cancellationToken);

        return new UserDirectoryPage(items, page, pageSize, total, totalPages, invitedCount, activatedCount);
    }

    public async Task<User> CreateUserAsync(
        string displayName,
        string email,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = Normalize(email);
        if (await dbContext.Users.AnyAsync(user => user.EmailNormalized == normalizedEmail, cancellationToken))
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

        dbContext.Users.Add(user);
        await SaveUniqueAsync(cancellationToken);

        // Queue the invitation durably rather than calling Brevo inline: the account is already
        // committed, so a mail outage cannot roll it back. The outbox worker delivers with retry.
        user.InvitationSentAt = DateTimeOffset.UtcNow;
        await emailQueue.EnqueueInvitationAsync(user.Email, user.DisplayName, temporaryPassword, cancellationToken);
        return user;
    }

    public async Task<User> UpdateUserAsync(
        Guid userId,
        UserProfileUpdate update,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        var normalizedEmail = Normalize(update.Email);
        var emailChanged = !string.Equals(normalizedEmail, user.EmailNormalized, StringComparison.OrdinalIgnoreCase);
        if (emailChanged
            && await dbContext.Users.AnyAsync(candidate => candidate.EmailNormalized == normalizedEmail, cancellationToken))
        {
            throw new ConflictAppException("A user with this email address already exists.");
        }

        user.DisplayName = update.DisplayName.Trim();
        user.Email = update.Email.Trim();
        user.EmailNormalized = normalizedEmail;
        user.Role = update.Role;
        user.AvatarUrl = UserDirectory.NullIfBlank(update.AvatarUrl);
        user.PreferredTeamName = UserDirectory.NullIfBlank(update.PreferredTeamName);

        await SaveUniqueAsync(cancellationToken);
        return user;
    }

    public async Task<User> SetUserStatusAsync(Guid userId, AccountStatus status, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");
        user.Status = status;
        if (status == AccountStatus.Deactivated)
        {
            // Kill any token issued before deactivation immediately.
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<User> SendInvitationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        var temporaryPassword = CreateTemporaryPassword();
        user.PasswordHash = _hasher.Hash(temporaryPassword);
        user.MustChangePassword = true;
        user.InvitationSentAt = DateTimeOffset.UtcNow;
        // A re-invite must invalidate any session established with the previous credential.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await dbContext.SaveChangesAsync(cancellationToken);

        await emailQueue.EnqueueInvitationAsync(user.Email, user.DisplayName, temporaryPassword, cancellationToken);
        return user;
    }

    public bool VerifyPassword(User user, string password) =>
        _hasher.Verify(user.PasswordHash, password);

    public async Task ChangePasswordAsync(
        User user,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (!VerifyPassword(user, currentPassword))
        {
            throw new UnauthorizedAppException("The current password is incorrect.");
        }

        ApplyNewPassword(user, newPassword);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PasswordResetGrant?> CreatePasswordResetTokenAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = Normalize(email);
        var user = await dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.EmailNormalized == normalized, cancellationToken);
        if (user is null || user.Status != AccountStatus.Active)
        {
            return null;
        }

        var token = ResetTokens.Generate();
        dbContext.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = ResetTokens.Hash(token),
            ExpiresAt = DateTimeOffset.UtcNow.Add(ResetTokens.Lifetime),
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new PasswordResetGrant(user, token);
    }

    public async Task<User> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken)
    {
        var hash = ResetTokens.Hash(token);
        var now = DateTimeOffset.UtcNow;
        var grant = await dbContext.PasswordResetTokens
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        if (grant is null || !grant.IsUsable(now))
        {
            throw new UnauthorizedAppException("This password reset link is invalid or has expired.");
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == grant.UserId, cancellationToken)
            ?? throw new UnauthorizedAppException("This password reset link is invalid or has expired.");

        grant.ConsumedAt = now;
        ApplyNewPassword(user, newPassword);
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<User> RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    /// <summary>Sets a new password and rotates the security stamp so older tokens stop validating.</summary>
    private void ApplyNewPassword(User user, string newPassword)
    {
        user.PasswordHash = _hasher.Hash(newPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
    }

    private async Task SaveUniqueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // The unique normalized-email index is the authoritative guard; a concurrent insert
            // that beats the pre-check surfaces here as a conflict rather than a raw error.
            throw new ConflictAppException("A user with this email address already exists.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();

    private static string CreateTemporaryPassword() =>
        $"Dr7!{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
}
