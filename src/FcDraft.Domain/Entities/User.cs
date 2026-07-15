namespace FcDraft.Domain.Entities;

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public required string EmailNormalized { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Player;
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public bool MustChangePassword { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public DateTimeOffset? InvitationSentAt { get; set; }
}

public enum UserRole
{
    Player = 1,
    Admin = 2
}

public enum AccountStatus
{
    Active = 1,
    Deactivated = 2
}
