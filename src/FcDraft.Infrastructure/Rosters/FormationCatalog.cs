using FcDraft.Domain.Entities;

namespace FcDraft.Infrastructure.Rosters;

/// <summary>
/// The catalogue of selectable formations, each exposed as a roster template. A host picks one per
/// lobby (PR-11) and the draft snapshots its ordered slots at start (PR-10). Every formation is
/// 1 held slot + 11 starting positions (the formation shape) + 4 flexible bench subs = 16 slots and
/// shares the MVP 120s pick timer.
///
/// Positions are limited to those actually present in the FC 26 dataset
/// (GK, RB, LB, CB, CDM, CM, CAM, LM, RM, LW, RW, ST) so every starting slot is fillable and the
/// timer's auto-pick (§6.4) can always resolve a footballer — back-fives use LB/RB for the wide
/// defenders rather than wing-back codes the dataset does not carry. The default active formation is
/// the MVP 4-3-3 from DRAFT_RULES.
/// </summary>
public static class FormationCatalog
{
    public const int PickTimerSeconds = 120;

    /// <summary>The MVP 4-3-3 keeps its original id so existing references stay stable.</summary>
    public static readonly Guid DefaultId = new("00000000-0000-0000-0000-0000000000f1");

    public sealed record Formation(Guid Id, string Name, IReadOnlyList<string> StartingPositions);

    public sealed record SlotDefinition(int Order, RosterSlotType SlotType, string? Position, string Label);

    // Ordered attack → midfield → defence → GK, mirroring the MVP template's slot order.
    public static readonly IReadOnlyList<Formation> All =
    [
        new(DefaultId,  "MVP 4-3-3",         ["ST", "LW", "RW", "CM", "CM", "CM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x01),   "4-3-3 (holding)",   ["ST", "LW", "RW", "CM", "CM", "CDM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x02),   "4-4-2",             ["ST", "ST", "LM", "CM", "CM", "RM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x03),   "4-4-2 (holding)",   ["ST", "ST", "LM", "CDM", "CDM", "RM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x04),   "4-4-1-1",           ["ST", "CAM", "LM", "CM", "CM", "RM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x05),   "4-2-3-1",           ["ST", "CAM", "LM", "RM", "CDM", "CDM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x06),   "4-2-3-1 (wide)",    ["ST", "CAM", "LW", "RW", "CDM", "CDM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x07),   "4-1-2-1-2",         ["ST", "ST", "CAM", "CM", "CM", "CDM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x08),   "4-1-2-1-2 (wide)",  ["ST", "ST", "CAM", "LM", "RM", "CDM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x09),   "4-2-2-2",           ["ST", "ST", "CAM", "CAM", "CDM", "CDM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x0A),   "4-5-1",             ["ST", "LM", "CM", "CM", "CM", "RM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x0B),   "4-1-4-1",           ["ST", "LM", "CM", "CM", "RM", "CDM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x0C),   "4-3-2-1",           ["ST", "CAM", "CAM", "CM", "CM", "CM", "LB", "CB", "CB", "RB", "GK"]),
        new(Id(0x0D),   "3-5-2",             ["ST", "ST", "LM", "CM", "CM", "CM", "RM", "CB", "CB", "CB", "GK"]),
        new(Id(0x0E),   "3-4-3",             ["LW", "ST", "RW", "LM", "CM", "CM", "RM", "CB", "CB", "CB", "GK"]),
        new(Id(0x0F),   "3-4-2-1",           ["ST", "CAM", "CAM", "LM", "CM", "CM", "RM", "CB", "CB", "CB", "GK"]),
        new(Id(0x10),   "3-4-1-2",           ["ST", "ST", "CAM", "LM", "CM", "CM", "RM", "CB", "CB", "CB", "GK"]),
        new(Id(0x11),   "3-1-4-2",           ["ST", "ST", "LM", "CM", "CM", "RM", "CDM", "CB", "CB", "CB", "GK"]),
        new(Id(0x12),   "5-3-2",             ["ST", "ST", "CM", "CM", "CM", "LB", "RB", "CB", "CB", "CB", "GK"]),
        new(Id(0x13),   "5-2-1-2",           ["ST", "ST", "CAM", "CM", "CM", "LB", "RB", "CB", "CB", "CB", "GK"]),
        new(Id(0x14),   "5-4-1",             ["ST", "LM", "CM", "CM", "RM", "LB", "RB", "CB", "CB", "CB", "GK"]),
        new(Id(0x15),   "5-2-3",             ["LW", "ST", "RW", "CM", "CM", "LB", "RB", "CB", "CB", "CB", "GK"]),
    ];

    public static Formation Default => All[0];

    public static Formation? Find(Guid id) => All.FirstOrDefault(formation => formation.Id == id);

    /// <summary>1 held slot (order 0) + the 11 starting positions + 4 flexible bench subs.</summary>
    public static IReadOnlyList<SlotDefinition> Slots(Formation formation)
    {
        var slots = new List<SlotDefinition>
        {
            new(0, RosterSlotType.Held, null, "Held player"),
        };

        for (var index = 0; index < formation.StartingPositions.Count; index++)
        {
            var position = formation.StartingPositions[index];
            slots.Add(new SlotDefinition(index + 1, RosterSlotType.StartingPosition, position, position));
        }

        for (var sub = 1; sub <= 4; sub++)
        {
            slots.Add(new SlotDefinition(formation.StartingPositions.Count + sub, RosterSlotType.FlexBench, null, $"Sub {sub}"));
        }

        return slots;
    }

    // Stable, deterministic ids for the non-MVP formations (…0401 … …0415), distinct from the MVP id.
    private static Guid Id(int index) => new($"00000000-0000-0000-0000-0000000004{index:x2}");
}
