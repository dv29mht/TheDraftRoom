namespace FcDraft.Domain.Entities;

/// <summary>
/// A single-use password-reset grant. Only the SHA-256 hash of the emailed token is stored, so a
/// leaked database row cannot be used to reset a password. A token is valid while it is unconsumed
/// and unexpired.
/// </summary>
public sealed class PasswordResetToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid UserId { get; init; }
    public required string TokenHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConsumedAt { get; set; }

    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && now < ExpiresAt;
}
