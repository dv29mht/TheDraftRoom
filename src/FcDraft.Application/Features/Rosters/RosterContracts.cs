namespace FcDraft.Application.Features.Rosters;

public sealed record RosterSlotDto(int Order, string SlotType, string? Position, string Label);

public sealed record RosterTemplateSummary(
    Guid Id, string Name, bool IsActive, int PickTimerSeconds, int SlotCount, DateTimeOffset CreatedAt);

public sealed record RosterTemplateDetail(RosterTemplateSummary Summary, IReadOnlyList<RosterSlotDto> Slots);

public sealed record CreateRosterTemplateRequest(string Name, int PickTimerSeconds, IReadOnlyList<RosterSlotDto> Slots);

/// <summary>A club from the active dataset with its curated five-star Kick Off eligibility.</summary>
public sealed record ClubDto(Guid Id, string Name, string League, bool IsFiveStarEligible);
