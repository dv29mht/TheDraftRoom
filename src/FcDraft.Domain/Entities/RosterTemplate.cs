namespace FcDraft.Domain.Entities;

/// <summary>
/// A versioned, ordered roster template (PR-09). A draft snapshots the active template's ordered
/// slots and pick timer at start (PR-10), so later template edits cannot change an in-progress draft.
/// The MVP default is the locked 4-3-3 in DRAFT_RULES: 1 held + 11 starters + 4 flexible subs.
/// </summary>
public sealed class RosterTemplate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Per-team, per-position pick timer in seconds. MVP default 120 (PRD §6.4).</summary>
    public int PickTimerSeconds { get; set; } = 120;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public ICollection<RosterSlot> Slots { get; init; } = new List<RosterSlot>();
}

/// <summary>
/// One ordered slot in a roster template. Starting slots carry a concrete required position; the held
/// slot and flexible bench slots accept any eligible position (the held player must come from the
/// team's chosen five-star club — enforced during the draft, PR-14).
/// </summary>
public sealed class RosterSlot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid TemplateId { get; init; }

    /// <summary>Fill order. 0 = held (pre-draft round); 1..N = starters then flexible subs.</summary>
    public required int Order { get; init; }
    public required RosterSlotType SlotType { get; init; }

    /// <summary>Required position for a starting slot; null for held/flexible slots.</summary>
    public string? Position { get; init; }
    public required string Label { get; init; }
}

public enum RosterSlotType
{
    Held = 1,
    StartingPosition = 2,
    FlexBench = 3,
}
