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

    /// <summary>
    /// Rotated whenever every existing session must be invalidated — password change/reset,
    /// deactivation, admin security action, or an explicit "sign out everywhere". Embedded in each
    /// issued token and re-checked on every authenticated request, so rotating it revokes older tokens.
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Optional profile avatar URL. Null until the account sets one.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Optional preferred team/club name shown across draft surfaces. Null until set.</summary>
    public string? PreferredTeamName { get; set; }

    /// <summary>
    /// §9.9 email preference: opts out of OPTIONAL announcement-style emails (e.g. draft reminders).
    /// Security and essential service messages (invitations, cancellations, results, password resets)
    /// remain mandatory and ignore this flag — enforced server-side where sends are enqueued.
    /// </summary>
    public bool OptionalEmailOptOut { get; set; }

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
