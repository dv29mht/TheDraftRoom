namespace FcDraft.Domain.Entities;

/// <summary>
/// One persistent per-user notification (PR-20, PRD §9.9): invitations, reminders, cancellations, and
/// results. Rows are appended in the SAME transaction as the draft mutation that caused them (a rolled
/// back command never notifies), survive restarts, and deep-link to their draft via <see cref="DraftId"/>.
/// Distinct from the admin-only live activity centre (<c>/api/notifications</c>), which is ephemeral.
/// </summary>
public sealed class UserNotification
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The recipient. Every read/mark endpoint is scoped to this user — never enumerable across users.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Kind discriminator: draft.invited / draft.reminder / draft.cancelled / draft.completed.</summary>
    public required string Type { get; init; }

    public required string Title { get; init; }
    public required string Body { get; init; }

    /// <summary>The draft this notification deep-links to (/drafts/{id}); null for non-draft notices.</summary>
    public Guid? DraftId { get; init; }

    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
