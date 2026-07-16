namespace FcDraft.Domain.Entities;

/// <summary>
/// A durable, at-least-once email work item. The account transaction (user creation, reset request)
/// commits an outbox row instead of calling Brevo inline, so a Brevo outage can never roll back the
/// account change. A background worker delivers pending rows with retry/backoff. The
/// <see cref="Secret"/> (one-time password or reset token) is server-only and cleared once delivered.
/// </summary>
public sealed class EmailOutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required EmailKind Kind { get; init; }
    public required string ToEmail { get; init; }
    public required string ToName { get; init; }

    /// <summary>The one-time password or reset token to embed. Cleared after successful delivery.</summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Non-secret template payload for draft-lifecycle emails (PR-20): JSON of
    /// <c>{draftId, draftName, reason}</c>. Retained after delivery (unlike <see cref="Secret"/>) for
    /// observability; null for the account emails, which carry only a secret.
    /// </summary>
    public string? Payload { get; set; }

    public EmailOutboxStatus Status { get; set; } = EmailOutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; init; } = 6;

    /// <summary>Earliest time the worker may (re)attempt delivery; advanced by backoff on failure.</summary>
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last non-sensitive delivery error, for observability. Never contains the secret.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
}

public enum EmailKind
{
    Invitation = 1,
    PasswordReset = 2,

    // Draft-lifecycle templates (PR-20, §9.8). Stored as strings, so appending members is forward-safe.
    DraftInvitation = 3,
    DraftReminder = 4,
    DraftCancelled = 5,
    DraftCompleted = 6,
}

public enum EmailOutboxStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
}
