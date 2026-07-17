using FcDraft.Domain.Entities;

namespace FcDraft.Infrastructure.Rosters;

/// <summary>
/// The default active roster template — the locked MVP 4-3-3 from DRAFT_RULES: one held player, then
/// <c>ST → LW → RW → CM → CM → CM → LB → CB → CB → RB → GK</c>, then four flexible subs. This is the
/// default entry in <see cref="FormationCatalog"/>; the constants/slots here are kept for the seeder,
/// the in-memory service, and tests that reference the default directly.
/// </summary>
public static class DefaultRosterTemplate
{
    public const string TemplateName = "MVP 4-3-3";
    public const int PickTimerSeconds = FormationCatalog.PickTimerSeconds;

    public sealed record SlotDefinition(int Order, RosterSlotType SlotType, string? Position, string Label);

    public static IReadOnlyList<SlotDefinition> Slots() =>
        FormationCatalog.Slots(FormationCatalog.Default)
            .Select(slot => new SlotDefinition(slot.Order, slot.SlotType, slot.Position, slot.Label))
            .ToArray();
}
