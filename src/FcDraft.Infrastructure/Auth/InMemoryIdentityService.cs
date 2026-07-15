using System.Collections.Concurrent;
using System.Security.Cryptography;
using FcDraft.Application.Common.Exceptions;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace FcDraft.Infrastructure.Auth;

public sealed class InMemoryIdentityService : IIdentityService
{
    private readonly ConcurrentDictionary<string, User> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly PasswordHasher<User> _hasher = new();
    private readonly IInvitationEmailSender _invitationEmailSender;

    public InMemoryIdentityService(IInvitationEmailSender invitationEmailSender)
    {
        _invitationEmailSender = invitationEmailSender;
        AddDevelopmentUser("mdevansh@gmail.com", "Devansh Mehta", UserRole.Admin, "Dv@241429", mustChangePassword: false);
        AddDevelopmentUser("player@draftroom.dev", "Practice Player", UserRole.Player, "Player@2026", mustChangePassword: false);
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _users.TryGetValue(Normalize(email), out var user);
        return Task.FromResult(user);
    }

    public Task<IReadOnlyCollection<User>> ListUsersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyCollection<User> users = _users.Values.OrderBy(user => user.DisplayName).ToArray();
        return Task.FromResult(users);
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
        user.PasswordHash = _hasher.HashPassword(user, temporaryPassword);
        if (!_users.TryAdd(normalizedEmail, user))
        {
            throw new ConflictAppException("A user with this email address already exists.");
        }

        try
        {
            await _invitationEmailSender.SendAsync(
                user.Email,
                user.DisplayName,
                temporaryPassword,
                cancellationToken);
            user.InvitationSentAt = DateTimeOffset.UtcNow;
            return user;
        }
        catch
        {
            _users.TryRemove(normalizedEmail, out _);
            throw;
        }
    }

    public Task<User> UpdateUserAsync(
        Guid userId,
        string displayName,
        string email,
        UserRole role,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _users.Values.FirstOrDefault(candidate => candidate.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        var normalizedEmail = Normalize(email);
        var emailChanged = !string.Equals(normalizedEmail, user.EmailNormalized, StringComparison.OrdinalIgnoreCase);
        if (emailChanged && _users.ContainsKey(normalizedEmail))
        {
            throw new ConflictAppException("A user with this email address already exists.");
        }

        var previousKey = user.EmailNormalized;
        user.DisplayName = displayName.Trim();
        user.Email = email.Trim();
        user.EmailNormalized = normalizedEmail;
        user.Role = role;

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
        return Task.FromResult(user);
    }

    public async Task<User> SendInvitationAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _users.Values.FirstOrDefault(candidate => candidate.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");
        var temporaryPassword = CreateTemporaryPassword();
        await _invitationEmailSender.SendAsync(
            user.Email,
            user.DisplayName,
            temporaryPassword,
            cancellationToken);
        user.PasswordHash = _hasher.HashPassword(user, temporaryPassword);
        user.MustChangePassword = true;
        user.InvitationSentAt = DateTimeOffset.UtcNow;
        return user;
    }

    public Task<User> DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _users.Values.FirstOrDefault(candidate => candidate.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");
        if (!_users.TryRemove(user.EmailNormalized, out _))
        {
            throw new KeyNotFoundException("User not found.");
        }

        return Task.FromResult(user);
    }

    public bool VerifyPassword(User user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

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

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
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
        user.PasswordHash = _hasher.HashPassword(user, password);
        _users[user.EmailNormalized] = user;
    }

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();

    private static string CreateTemporaryPassword() =>
        $"Dr7!{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
}
