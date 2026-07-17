namespace FcDraft.Infrastructure.Rosters;

/// <summary>
/// Convenience accessors for the default active roster template — the locked MVP 4-3-3 from
/// DRAFT_RULES: one held player, then <c>ST → LW → RW → CM → CM → CM → LB → CB → CB → RB → GK</c>,
/// then four flexible subs. This is just the default entry in <see cref="FormationCatalog"/> surfaced
/// by name for the seeder, the in-memory service, and tests — the catalogue is the single source of
/// truth, so there is no duplicated name/slot data here.
/// </summary>
public static class DefaultRosterTemplate
{
    public static string TemplateName => FormationCatalog.Default.Name;
    public const int PickTimerSeconds = FormationCatalog.PickTimerSeconds;

    public static IReadOnlyList<FormationCatalog.SlotDefinition> Slots() =>
        FormationCatalog.Slots(FormationCatalog.Default);
}
