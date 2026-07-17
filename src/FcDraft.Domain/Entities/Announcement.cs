namespace FcDraft.Domain.Entities;

/// <summary>
/// An immutable record of one admin announcement campaign (PR-21, §9.8): what was sent, to whom
/// (the audience definition and resolved counts), by which admin, and when. The per-recipient
/// delivery status lives on the outbox rows stamped with this record's <see cref="Id"/> as their
/// campaign id — this row is the campaign metadata §9.8 requires. Append-only: it is written once
/// when the confirmed send is accepted and never updated or deleted through any normal API.
/// </summary>
public sealed class Announcement
{
    /// <summary>The campaign id. Stamped on every outbox email row this announcement enqueued.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Subject { get; init; }
    public required string Body { get; init; }

    public required AnnouncementAudience Audience { get; init; }

    /// <summary>The draft whose participants were addressed; null for the all-players audience.</summary>
    public Guid? DraftId { get; init; }

    /// <summary>Human-readable audience definition at send time (e.g. “Participants of ‘Friday Night’”).</summary>
    public required string AudienceLabel { get; init; }

    /// <summary>How many recipients received the in-app notification (the full resolved audience).</summary>
    public required int RecipientCount { get; init; }

    /// <summary>How many announcement emails were enqueued (recipients minus §9.9 opt-outs).</summary>
    public required int EmailCount { get; init; }

    /// <summary>How many recipients had opted out of optional emails (in-app notification only).</summary>
    public required int OptedOutCount { get; init; }

    public required Guid RequestedByUserId { get; init; }

    /// <summary>The requesting admin's email, snapshotted for attribution even if the account changes.</summary>
    public required string RequestedByEmail { get; init; }

    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum AnnouncementAudience
{
    // Stored as strings, so appending members is forward-safe.
    AllPlayers = 1,
    DraftParticipants = 2,
}
