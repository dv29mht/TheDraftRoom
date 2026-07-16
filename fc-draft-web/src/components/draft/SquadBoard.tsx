import { Shield } from 'lucide-react'
import type { DraftDetail, DraftPick } from '../../types/draft'

// A per-team squad view driven by the frozen roster slots and the accepted picks — the held player then each
// slot in order, filled or still open. Shared by the paused/completed/cancelled stages.
export function SquadBoard({ detail }: { detail: DraftDetail }) {
  const teams = [...detail.teams].sort((a, b) => (a.spinnerRank ?? 0) - (b.spinnerRank ?? 0))
  const slots = [...detail.slots].sort((a, b) => a.order - b.order)
  const pickAt = (teamId: string, slotOrder: number): DraftPick | undefined =>
    detail.picks.find((pick) => pick.teamId === teamId && pick.slotOrder === slotOrder)

  return (
    <div className="squad-board" aria-label="Draft board">
      {teams.map((team) => (
        <div key={team.id} className="team-card squad-card">
          <div className="team-card-head">
            {team.spinnerRank != null && <span className="spinner-rank">{team.spinnerRank}</span>}
            <strong>{team.name}</strong>
            {team.selectedClubName && <span className="status-pill status-joined"><Shield aria-hidden="true" /> {team.selectedClubName}</span>}
          </div>
          <ul className="squad-slots">
            {slots.map((slot) => {
              const filled = pickAt(team.id, slot.order)
              return (
                <li key={slot.order} className={`squad-slot${filled ? ' is-filled' : ''}`}>
                  <span className="squad-slot-label">{slot.order === 0 ? 'Held' : slot.label}</span>
                  {filled
                    ? <span className="squad-slot-pick">{filled.footballerName} · {filled.footballerOverall}</span>
                    : <span className="squad-slot-empty">—</span>}
                </li>
              )
            })}
          </ul>
        </div>
      ))}
    </div>
  )
}
