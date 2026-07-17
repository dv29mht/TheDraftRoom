namespace FcDraft.Domain.Entities;

/// <summary>
/// An append-only record of a security-relevant or admin action (sign-in, failed sign-in, credential
/// reset, activation, deactivation, password change, session revocation; and, since PR-21, the §9.10
/// admin changes — user create/update, dataset and template activation, five-star curation, and bulk
/// announcement requests). Never records secrets or passwords. PR-05 records the sign-in events;
/// PR-21 records the admin actions and adds the admin audit views over both.
/// </summary>
public sealed class SecurityAuditEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The account the event concerns, when known (a failed sign-in may reference no account). For
    /// the PR-21 admin actions this is the ACTING admin — attribution — while <see cref="Detail"/>
    /// names the target.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>The email the action targeted (the actor's, for admin actions). Never a password.</summary>
    public string? Email { get; init; }

    public required SecurityAuditAction Action { get; init; }

    /// <summary>Optional non-sensitive context (for example a lockout reason). Never a credential.</summary>
    public string? Detail { get; init; }

    public string? IpAddress { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum SecurityAuditAction
{
    SignInSucceeded = 1,
    SignInFailed = 2,
    SignInLockedOut = 3,
    PasswordChanged = 4,
    PasswordResetRequested = 5,
    PasswordReset = 6,
    SessionsRevoked = 7,
    AccountActivated = 8,
    AccountDeactivated = 9,

    // §9.10 admin actions (PR-21). Stored as strings, so appending members is forward-safe.
    UserCreated = 10,
    UserUpdated = 11,
    UserInvited = 12,
    DatasetImported = 13,
    DatasetActivated = 14,
    TemplateCreated = 15,
    TemplateActivated = 16,
    ClubFiveStarChanged = 17,
    AnnouncementSent = 18,
}
