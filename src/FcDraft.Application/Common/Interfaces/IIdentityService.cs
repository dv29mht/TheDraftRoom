using FcDraft.Domain.Entities;

namespace FcDraft.Application.Common.Interfaces;

public interface IIdentityService
{
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<User>> ListUsersAsync(CancellationToken cancellationToken);
    Task<User> CreateUserAsync(string displayName, string email, UserRole role, CancellationToken cancellationToken);
    Task<User> UpdateUserAsync(Guid userId, string displayName, string email, UserRole role, CancellationToken cancellationToken);
    Task<User> SetUserStatusAsync(Guid userId, AccountStatus status, CancellationToken cancellationToken);
    Task<User> SendInvitationAsync(Guid userId, CancellationToken cancellationToken);
    Task<User> DeleteUserAsync(Guid userId, CancellationToken cancellationToken);
    bool VerifyPassword(User user, string password);
    Task ChangePasswordAsync(User user, string currentPassword, string newPassword, CancellationToken cancellationToken);
}

public interface ITokenService
{
    TokenResult Create(User user);
}

public sealed record TokenResult(string AccessToken, DateTimeOffset ExpiresAt);
