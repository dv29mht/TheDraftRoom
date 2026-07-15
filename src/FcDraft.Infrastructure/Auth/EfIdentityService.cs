using System.Security.Cryptography;
using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using FcDraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FcDraft.Infrastructure.Auth;

/// <summary>
/// PostgreSQL-backed identity service. Preserves the exact behavior of the in-memory foundation
/// (invite with a one-time password, deactivation, forced first-login change) while persisting the
/// user directory so it survives an API restart. The richer durable directory — DB-side
/// pagination, historical retention, removal of hard delete — is PR-04.
/// </summary>
public sealed class EfIdentityService(
    FcDraftDbContext dbContext,
    IInvitationEmailSender invitationEmailSender)
    : IIdentityService
{
    private readonly PasswordHasher<User> _hasher = new();

    public async Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = Normalize(email);
        return await dbContext.Users
            .FirstOrDefaultAsync(user => user.EmailNormalized == normalized, cancellationToken);
    }

    public async Task<IReadOnlyCollection<User>> ListUsersAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.DisplayName)
            .ToArrayAsync(cancellationToken);
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
        user.PasswordHash = _hasher.HashPassword(user, temporaryPassword);

        dbContext.Users.Add(user);
        await SaveUniqueAsync(cancellationToken);

        try
        {
            await invitationEmailSender.SendAsync(
                user.Email,
                user.DisplayName,
                temporaryPassword,
                cancellationToken);
        }
        catch
        {
            // Keep the persisted directory consistent with the delivered invitations: if the
            // email transport fails, the account is rolled back exactly as the in-memory store did.
            dbContext.Users.Remove(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        user.InvitationSentAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<User> UpdateUserAsync(
        Guid userId,
        string displayName,
        string email,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        var normalizedEmail = Normalize(email);
        var emailChanged = !string.Equals(normalizedEmail, user.EmailNormalized, StringComparison.OrdinalIgnoreCase);
        if (emailChanged
            && await dbContext.Users.AnyAsync(candidate => candidate.EmailNormalized == normalizedEmail, cancellationToken))
        {
            throw new ConflictAppException("A user with this email address already exists.");
        }

        user.DisplayName = displayName.Trim();
        user.Email = email.Trim();
        user.EmailNormalized = normalizedEmail;
        user.Role = role;

        await SaveUniqueAsync(cancellationToken);
        return user;
    }

    public async Task<User> SetUserStatusAsync(Guid userId, AccountStatus status, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");
        user.Status = status;
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<User> SendInvitationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        var temporaryPassword = CreateTemporaryPassword();
        await invitationEmailSender.SendAsync(
            user.Email,
            user.DisplayName,
            temporaryPassword,
            cancellationToken);

        user.PasswordHash = _hasher.HashPassword(user, temporaryPassword);
        user.MustChangePassword = true;
        user.InvitationSentAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<User> DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");
        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public bool VerifyPassword(User user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

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

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
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
