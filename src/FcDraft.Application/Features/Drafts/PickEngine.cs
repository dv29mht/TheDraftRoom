using FcDraft.Application.Common.Interfaces;
using FcDraft.Domain.Entities;

namespace FcDraft.Application.Features.Drafts;

/// <summary>
/// The single accept-a-position-pick path (PR-15/PR-16). Both a teammate's <see cref="SubmitPickCommand"/>
/// and the timer's expiry auto-pick run through <see cref="Accept"/>, so eligibility (slot position match,
/// availability) and the turn bookkeeping (advance the persisted clock anchor, complete on the final slot)
/// are enforced once and can never drift apart. <see cref="ResolveAutoPickAsync"/> is the deterministic
/// §6.4 selection: the catalog already lists eligible footballers highest overall → name → stable id — the
/// exact DRAFT_RULES tie-break — so the auto-pick is simply the first of that ordering not already taken.
/// </summary>
internal static class PickEngine
{
    /// <summary>A pool large enough that the first available entry always exists while any slot is open.</summary>
    private const int AutoPickPoolSize = 500;

    /// <summary>
    /// Accepts one pick for the active (team, slot): validates the footballer fills the slot and is still
    /// available, records the pick (as <c>PickAccepted</c>, or <c>PickAutoSelected</c> when
    /// <paramref name="isAutoPick"/>), then either completes the draft (final slot) or restarts the turn
    /// clock at <paramref name="nextTurnAnchor"/> — "now" for a live pick, the expired deadline for a
    /// catch-up auto-pick so each missed turn consumed exactly its allotted seconds.
    /// </summary>
    public static void Accept(
        Draft draft,
        DraftTeam activeTeam,
        DraftRosterSlot slot,
        CatalogFootballer footballer,
        Guid? actorParticipantId,
        Guid? actorUserId,
        bool isAutoPick,
        DateTimeOffset nextTurnAnchor)
    {
        var acceptsAny = slot.SlotType == RosterSlotType.FlexBench || slot.Position is null;
        if (!acceptsAny && !footballer.Positions.Any(position => string.Equals(position, slot.Position, StringComparison.OrdinalIgnoreCase)))
        {
            throw FormationGuards.Validation("footballer", $"{footballer.Name} cannot play {slot.Position}.");
        }
        if (draft.Picks.Any(pick => pick.FootballerId == footballer.Id))
        {
            throw FormationGuards.Validation("footballer", "That footballer has already been taken.");
        }

        draft.AcceptPick(
            activeTeam.Id,
            slot.Order,
            new DraftPickFootballer(footballer.Id, footballer.Name, footballer.Overall, footballer.Positions.FirstOrDefault()),
            actorParticipantId,
            actorUserId,
            isAutoPick);

        // The final slot fills the last squad — complete the draft; otherwise the next turn's clock starts.
        if (DraftTurn.ActivePosition(draft) is null)
        {
            draft.CompleteDraft(actorUserId);
        }
        else
        {
            draft.StartTurnClock(nextTurnAnchor);
        }
    }

    /// <summary>
    /// The deterministic auto-pick for the active slot (PRD §6.4, DRAFT_RULES decision 7): the first entry
    /// of the catalog's highest-overall → name → id ordering that fills the slot and is not already held or
    /// drafted. Null only when the pinned pool is exhausted for this slot (no eligible footballer remains).
    /// </summary>
    public static async Task<CatalogFootballer?> ResolveAutoPickAsync(
        Draft draft, DraftRosterSlot slot, IDraftCatalog catalog, CancellationToken cancellationToken)
    {
        var acceptsAny = slot.SlotType == RosterSlotType.FlexBench || slot.Position is null;
        var pool = await catalog.ListFootballersAsync(
            draft.PinnedDatasetVersionId,
            new CatalogFootballerFilter(Position: acceptsAny ? null : slot.Position, Take: AutoPickPoolSize),
            cancellationToken);

        var taken = draft.Picks.Select(pick => pick.FootballerId).ToHashSet();
        return pool.FirstOrDefault(footballer => !taken.Contains(footballer.Id));
    }
}
