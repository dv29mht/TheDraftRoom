using FcDraft.Domain.Entities;

namespace FcDraft.Infrastructure.Rosters;

/// <summary>
/// The locked MVP roster template from DRAFT_RULES: 4-3-3, one held player, then
/// <c>ST → LW → RW → CM → CM → CM → LB → CB → CB → RB → GK</c>, then four flexible subs. Shared by
/// the database seeder and the in-memory service so both configurations expose the same default.
/// </summary>
public static class DefaultRosterTemplate
{
    public const string TemplateName = "MVP 4-3-3";
    public const int PickTimerSeconds = 120;

    public sealed record SlotDefinition(int Order, RosterSlotType SlotType, string? Position, string Label);

    private static readonly string[] StartingPositions =
        ["ST", "LW", "RW", "CM", "CM", "CM", "LB", "CB", "CB", "RB", "GK"];

    public static IReadOnlyList<SlotDefinition> Slots()
    {
        var slots = new List<SlotDefinition>
        {
            new(0, RosterSlotType.Held, null, "Held player"),
        };

        for (var index = 0; index < StartingPositions.Length; index++)
        {
            var position = StartingPositions[index];
            slots.Add(new SlotDefinition(index + 1, RosterSlotType.StartingPosition, position, position));
        }

        for (var sub = 1; sub <= 4; sub++)
        {
            slots.Add(new SlotDefinition(StartingPositions.Length + sub, RosterSlotType.FlexBench, null, $"Sub {sub}"));
        }

        return slots;
    }
}
