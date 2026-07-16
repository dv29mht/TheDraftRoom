import { Shield } from 'lucide-react'
import { useState } from 'react'
import type { DraftDetail, DraftPick } from '../../types/draft'
import { chronologicalPicks } from './common'

type RailView = 'squad' | 'recent' | 'history'

/**
 * §9.6's "every team, without rendering all 10 full squads at once": a compact team rail (one chip per
 * team with live pick counts and the on-the-clock marker), plus a single focused squad, the recent picks,
 * and the full pick history — one view at a time, so the room stays readable at 375px.
 */
export function TeamRail({ detail, userId }: { detail: DraftDetail; userId: string | undefined }) {
  const teams = [...detail.teams].sort((a, b) => (a.spinnerRank ?? 0) - (b.spinnerRank ?? 0))
  const slots = [...detail.slots].sort((a, b) => a.order - b.order)
  const myTeam = teams.find((team) => userId != null && team.memberUserIds.includes(userId))
  const [view, setView] = useState<RailView>('squad')
  const [focusedTeamId, setFocusedTeamId] = useState<string | null>(null)
  const focused = teams.find((team) => team.id === focusedTeamId) ?? myTeam ?? teams[0]
  const ordered = chronologicalPicks(detail)
  const teamName = (teamId: string) => teams.find((team) => team.id === teamId)?.name ?? 'Team'
  const slotLabel = (slotOrder: number) => slotOrder === 0 ? 'Held' : slots.find((slot) => slot.order === slotOrder)?.label ?? `Slot ${slotOrder}`
  const picksOf = (teamId: string) => detail.picks.filter((pick) => pick.teamId === teamId)

  const pickRow = (pick: DraftPick, index: number, newest: boolean) => (
    <li key={`${pick.teamId}-${pick.slotOrder}`} className={`pick-history-row${newest ? ' is-newest' : ''}`}>
      <span className="pick-history-number">#{index + 1}</span>
      <div>
        <strong>{pick.footballerName} · {pick.footballerOverall}</strong>
        <small>{teamName(pick.teamId)} — {slotLabel(pick.slotOrder)}</small>
      </div>
    </li>
  )

  return (
    <div className="team-rail-panel">
      <div className="team-rail" role="group" aria-label="Teams">
        {teams.map((team) => {
          const active = detail.turn.activeTeamId === team.id
          const isFocused = focused?.id === team.id
          return (
            <button
              key={team.id}
              type="button"
              className={`team-rail-chip${isFocused ? ' is-focused' : ''}${active ? ' is-on-clock' : ''}`}
              aria-pressed={isFocused}
              onClick={() => { setFocusedTeamId(team.id); setView('squad') }}
            >
              {team.spinnerRank != null && <span className="spinner-rank">{team.spinnerRank}</span>}
              <span className="team-rail-name">
                <strong>{team.name}{team.id === myTeam?.id ? ' · You' : ''}</strong>
                <small>{picksOf(team.id).length}/{slots.length} picks{active ? ' · On the clock' : ''}</small>
              </span>
            </button>
          )
        })}
      </div>

      <div className="rail-view-tabs" role="group" aria-label="Squad views">
        {([['squad', 'Squad'], ['recent', 'Recent picks'], ['history', 'Full history']] as [RailView, string][]).map(([key, label]) => (
          <button
            key={key}
            type="button"
            className={`rail-view-tab${view === key ? ' is-active' : ''}`}
            aria-pressed={view === key}
            onClick={() => setView(key)}
          >
            {label}
          </button>
        ))}
      </div>

      {view === 'squad' && focused && (
        <div className="team-card squad-card focused-squad" aria-label={`${focused.name} squad`}>
          <div className="team-card-head">
            {focused.spinnerRank != null && <span className="spinner-rank">{focused.spinnerRank}</span>}
            <strong>{focused.name}</strong>
            {focused.selectedClubName && <span className="status-pill status-joined"><Shield aria-hidden="true" /> {focused.selectedClubName}</span>}
          </div>
          <ul className="squad-slots">
            {slots.map((slot) => {
              const filled = detail.picks.find((pick) => pick.teamId === focused.id && pick.slotOrder === slot.order)
              const isOpenSlot = detail.turn.activeTeamId === focused.id && detail.turn.activeSlotOrder === slot.order
              return (
                <li key={slot.order} className={`squad-slot${filled ? ' is-filled' : ''}${isOpenSlot ? ' is-open-turn' : ''}`}>
                  <span className="squad-slot-label">{slot.order === 0 ? 'Held' : slot.label}</span>
                  {filled
                    ? <span className="squad-slot-pick">{filled.footballerName} · {filled.footballerOverall}</span>
                    : <span className="squad-slot-empty">{isOpenSlot ? 'Picking now…' : '—'}</span>}
                </li>
              )
            })}
          </ul>
        </div>
      )}

      {view === 'recent' && (
        <ul className="pick-history" aria-label="Recent picks">
          {ordered.length === 0 && <li className="invite-empty">No picks yet.</li>}
          {[...ordered].slice(-8).reverse().map((pick) => {
            const index = ordered.indexOf(pick)
            return pickRow(pick, index, index === ordered.length - 1)
          })}
        </ul>
      )}

      {view === 'history' && (
        <ul className="pick-history pick-history-full" aria-label="Full pick history">
          {ordered.length === 0 && <li className="invite-empty">No picks yet.</li>}
          {ordered.map((pick, index) => pickRow(pick, index, false))}
        </ul>
      )}
    </div>
  )
}
