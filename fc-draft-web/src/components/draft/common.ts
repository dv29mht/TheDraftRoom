import { useEffect } from 'react'
import type { DraftDetail, DraftPick } from '../../types/draft'

/** Runs one optimistic-version mutation against the lobby and applies the returned snapshot. */
export type StageMutate = (action: (version: number) => Promise<DraftDetail>) => Promise<void>

/**
 * Reconstructs the chronological pick order from the deterministic turn rules — the held round (slot 0)
 * runs in straight spinner order, then each position round r (slot order r) snakes: odd rounds ascend by
 * rank, even rounds descend. Picks carry no timestamp, so this derivation IS the order the server
 * accepted them in; it never re-derives *whose turn is next* (that stays server-authoritative).
 */
export function chronologicalPicks(detail: DraftDetail): DraftPick[] {
  const rankOf = (teamId: string) =>
    detail.teams.find((team) => team.id === teamId)?.spinnerRank ?? Number.MAX_SAFE_INTEGER
  return [...detail.picks].sort((a, b) => {
    if (a.slotOrder !== b.slotOrder) return a.slotOrder - b.slotOrder
    const ascending = a.slotOrder === 0 || a.slotOrder % 2 === 1
    return ascending ? rankOf(a.teamId) - rankOf(b.teamId) : rankOf(b.teamId) - rankOf(a.teamId)
  })
}

/**
 * While a live draft stage is on screen, the room replaces the global bottom navigation with its own
 * sticky action area (PRD §8.3) so a stray tap cannot exit the draft. CSS keys off this body class.
 */
export function useDraftRoomChrome() {
  useEffect(() => {
    document.body.classList.add('draft-room-live')
    return () => document.body.classList.remove('draft-room-live')
  }, [])
}
