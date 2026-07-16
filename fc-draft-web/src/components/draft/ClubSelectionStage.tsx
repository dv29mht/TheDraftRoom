import { Play, Shield, Star } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { draftsApi } from '../../services/api'
import type { CatalogFootballer, DraftBoard, DraftDetail } from '../../types/draft'
import type { StageMutate } from './common'
import { PickConfirmSheet } from './PickConfirmSheet'
import type { PendingPick } from './PickConfirmSheet'

// The pre-draft five-star club + protected-player round (PR-14). Straight spinner order: the active team
// picks a five-star club, then protects a 75+ player from it — behind the same §9.6 confirmation sheet as
// a room pick. The pools come from the server board so the client never re-derives eligibility or order.
export function ClubSelectionStage({ detail, isHost, busy, userId, nameOf, mutate }: {
  detail: DraftDetail
  isHost: boolean
  busy: boolean
  userId: string | undefined
  nameOf: (id: string) => string
  mutate: StageMutate
}) {
  const summary = detail.summary
  const turn = detail.turn
  const [selectedClub, setSelectedClub] = useState('')
  const [board, setBoard] = useState<DraftBoard | null>(null)
  const [pendingProtect, setPendingProtect] = useState<PendingPick | null>(null)
  const requestSeq = useRef(0)

  // The board refreshes on every accepted mutation; choosing a club also fetches that club's
  // still-available 75+ pool for the held pick.
  useEffect(() => {
    const seq = ++requestSeq.current
    void draftsApi
      .board(summary.id, selectedClub ? { clubId: selectedClub } : undefined)
      .then((next) => { if (requestSeq.current === seq) setBoard(next) })
      .catch(() => { /* REST fallback: the next pushed snapshot re-triggers this fetch */ })
  }, [summary.id, summary.version, selectedClub])

  const isMyTurn = board?.isMyTurn ?? (userId != null && turn.activeTeamMemberUserIds.includes(userId))
  const teams = [...detail.teams].sort((a, b) => (a.spinnerRank ?? 0) - (b.spinnerRank ?? 0))
  const allChosen = teams.length > 0 && teams.every((team) => team.selectedClubId != null)
  const heldOf = (teamId: string) => detail.picks.find((pick) => pick.teamId === teamId && pick.slotOrder === 0)
  const clubName = board?.availableClubs.find((club) => club.id === selectedClub)?.name

  const protect = (footballer: CatalogFootballer) => setPendingProtect({
    footballerId: footballer.id,
    name: footballer.name,
    overall: footballer.overall,
    positions: footballer.positions,
    clubName: clubName ?? footballer.clubName,
  })

  const confirmProtect = (pick: PendingPick) => {
    if (!selectedClub) return
    void mutate((version) => draftsApi.selectClubAndProtect(summary.id, selectedClub, pick.footballerId, version))
      .then(() => { setPendingProtect(null); setSelectedClub('') })
  }

  return (
    <section className="panel formation-panel">
      <div className="panel-heading"><div><span className="eyebrow">Club selection</span><h2>Five-star club &amp; protected player</h2></div><Star aria-hidden="true" /></div>

      <ul className="team-roster" aria-label="Club selection order">
        {teams.map((team) => {
          const held = heldOf(team.id)
          const active = turn.activeTeamId === team.id
          return (
            <li key={team.id} className={`team-card${active ? ' is-active' : ''}`}>
              <div className="team-card-head">
                {team.spinnerRank != null && <span className="spinner-rank">{team.spinnerRank}</span>}
                <strong>{team.name}</strong>
                {team.selectedClubId
                  ? <span className="status-pill status-joined"><Shield aria-hidden="true" /> {team.selectedClubName ?? 'Club chosen'}</span>
                  : <span className={`status-pill ${active ? 'status-invited' : ''}`}>{active ? 'Choosing…' : 'Waiting'}</span>}
              </div>
              <div className="team-members">
                {held ? <>Protected: <strong>{held.footballerName}</strong> · {held.footballerOverall}</> : team.memberUserIds.map(nameOf).join(' · ')}
              </div>
            </li>
          )
        })}
      </ul>

      {!allChosen && (
        <p className="coming-soon-note" role="status">
          {isMyTurn ? "It's your turn — choose a five-star club, then protect a player from it." : `Waiting for ${turn.activeTeamName ?? 'the next team'} to choose.`}
        </p>
      )}

      {isMyTurn && !allChosen && (
        <div className="club-picker">
          <label>
            <span>Five-star club</span>
            <select value={selectedClub} onChange={(event) => setSelectedClub(event.target.value)} aria-label="Five-star club" disabled={busy}>
              <option value="">Choose a club…</option>
              {(board?.availableClubs ?? []).map((club) => <option key={club.id} value={club.id}>{club.name} · {club.league}</option>)}
            </select>
          </label>
          {selectedClub && (
            <ul className="pick-pool" aria-label="Players you can protect">
              {(board?.eligibleFootballers ?? []).length === 0 && <li className="invite-empty">No available 75+ players from that club.</li>}
              {(board?.eligibleFootballers ?? []).map((footballer) => (
                <li key={footballer.id}>
                  <div><strong>{footballer.name}</strong><small>{footballer.overall} · {footballer.positions.join('/')}</small></div>
                  <button className="ghost-button" type="button" disabled={busy} onClick={() => protect(footballer)}><Shield /> Protect</button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {isHost && (
        <div className="host-actions">
          <button className="primary-button compact" type="button" disabled={busy || !allChosen} onClick={() => void mutate((version) => draftsApi.openPositionDraft(summary.id, version))}><Play /> Open position draft</button>
        </div>
      )}
      {isHost && !allChosen && <p className="coming-soon-note">Every team must choose a club and protect a player before the position draft can begin.</p>}

      {pendingProtect && (
        <PickConfirmSheet
          pick={pendingProtect}
          teamName={turn.activeTeamName ?? 'your team'}
          slotLabel={turn.activeSlotLabel ?? 'Held player'}
          verb="Protect"
          busy={busy}
          onConfirm={() => confirmProtect(pendingProtect)}
          onCancel={() => setPendingProtect(null)}
        />
      )}
    </section>
  )
}
