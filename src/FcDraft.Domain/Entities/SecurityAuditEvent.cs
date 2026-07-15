namespace FcDraft.Domain.Entities;

/// <summary>
/// An append-only record of a security-relevant action (sign-in, failed sign-in, credential reset,
/// activation, deactivation, password change, session revocation). Never records secrets or
/// passwords. PR-05 records these; the admin audit views over them arrive in PR-21.
/// </summary>
public sealed class SecurityAuditEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The account the event concerns, when known (a failed sign-in may reference no account).</summary>
    public Guid? UserId { get; init; }

    /// <summary>The email the action targeted, normalized. Stored for attribution, never a password.</summary>
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
}
