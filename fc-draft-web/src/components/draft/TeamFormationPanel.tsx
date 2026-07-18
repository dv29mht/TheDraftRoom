import { Check, Flag, Shuffle, UserPlus, Users, X } from 'lucide-react'
import { useMemo, useState } from 'react'
import { draftsApi } from '../../services/api'
import type { DraftDetail, DraftSeed, LobbyParticipant, TeamFormationInput } from '../../types/draft'
import type { StageMutate } from './common'
import { RequirementSummary, TeamRoster } from './TeamRoster'

export function TeamFormationPanel({ detail, isHost, busy, canReady, me, nameOf, mutate }: {
  detail: DraftDetail
  isHost: boolean
  busy: boolean
  canReady: boolean
  me: LobbyParticipant | undefined
  nameOf: (id: string) => string
  mutate: StageMutate
}) {
  const summary = detail.summary
  const requirements = detail.startRequirements
  const is2v2 = summary.format === '2v2'
  const [seed1Sel, setSeed1Sel] = useState('')
  const [seed2Sel, setSeed2Sel] = useState('')

  const assignedIds = useMemo(
    () => new Set(detail.teams.flatMap((team) => team.memberUserIds)),
    [detail.teams],
  )
  const availableSeed1 = detail.participants.filter((participant) => participant.seed === 'Seed1' && !assignedIds.has(participant.userId))
  const availableSeed2 = detail.participants.filter((participant) => participant.seed === 'Seed2' && !assignedIds.has(participant.userId))

  const teamsToInput = (): TeamFormationInput[] =>
    detail.teams.map((team) => ({ name: team.name, memberUserIds: team.memberUserIds }))

  const addTeam = () => {
    if (!seed1Sel || !seed2Sel) return
    const teams = [...teamsToInput(), { memberUserIds: [seed1Sel, seed2Sel] }]
    setSeed1Sel('')
    setSeed2Sel('')
    void mutate((version) => draftsApi.formTeams(summary.id, teams, version))
  }

  const removeTeam = (teamId: string) => {
    const teams = detail.teams.filter((team) => team.id !== teamId).map((team) => ({ name: team.name, memberUserIds: team.memberUserIds }))
    void mutate((version) => draftsApi.formTeams(summary.id, teams, version))
  }

  return (
    <section className="panel formation-panel">
      <div className="panel-heading"><div><span className="eyebrow">Team formation</span><h2>{is2v2 ? 'Seed and pair teams' : 'Confirm solo teams'}</h2></div><Users aria-hidden="true" /></div>

      {is2v2 && isHost && (
        <div className="seed-assignment">
          <div className="pairing-callout">
            <div className="pairing-callout-text">
              <span className="eyebrow"><Shuffle aria-hidden="true" /> Random pairings</span>
              <strong>Shuffle everyone into 2v2 teams</strong>
              <p>Teams are drawn at random — no one picks their team-mate. You can still adjust seeds and pairs by hand below.</p>
            </div>
            <button className="primary-button compact" type="button" disabled={busy} onClick={() => void mutate((version) => draftsApi.randomizeTeams(summary.id, version))}>
              <Shuffle /> Shuffle pairings
            </button>
          </div>
          <p className="step-label">Or assign each player Seed 1 or Seed 2, then pair them into teams by hand.</p>
          <ul className="participant-list">
            {detail.participants.map((participant) => (
              <li key={participant.userId} className="participant-row seed-row">
                <span className="participant-avatar">{(participant.displayName ?? '?').slice(0, 2).toUpperCase()}</span>
                <div className="participant-identity"><strong>{participant.displayName ?? 'Unknown player'}</strong></div>
                <div className="seed-toggle" role="group" aria-label={`Seed for ${participant.displayName ?? 'player'}`}>
                  {(['Seed1', 'Seed2'] as DraftSeed[]).map((seed) => (
                    <button
                      key={seed}
                      type="button"
                      className={`seed-button${participant.seed === seed ? ' is-active' : ''}`}
                      disabled={busy}
                      aria-pressed={participant.seed === seed}
                      onClick={() => void mutate((version) => draftsApi.assignSeed(summary.id, participant.userId, seed, version))}
                    >
                      {seed === 'Seed1' ? 'Seed 1' : 'Seed 2'}
                    </button>
                  ))}
                </div>
              </li>
            ))}
          </ul>

          <div className="team-builder">
            <label>
              <span>Seed 1</span>
              <select value={seed1Sel} onChange={(event) => setSeed1Sel(event.target.value)} aria-label="Seed 1 player" disabled={busy}>
                <option value="">Choose Seed 1…</option>
                {availableSeed1.map((participant) => <option key={participant.userId} value={participant.userId}>{participant.displayName}</option>)}
              </select>
            </label>
            <label>
              <span>Seed 2</span>
              <select value={seed2Sel} onChange={(event) => setSeed2Sel(event.target.value)} aria-label="Seed 2 player" disabled={busy}>
                <option value="">Choose Seed 2…</option>
                {availableSeed2.map((participant) => <option key={participant.userId} value={participant.userId}>{participant.displayName}</option>)}
              </select>
            </label>
            <button className="ghost-button" type="button" disabled={busy || !seed1Sel || !seed2Sel} onClick={addTeam}><UserPlus /> Add team</button>
          </div>
        </div>
      )}

      {!is2v2 && isHost && detail.teams.length === 0 && (
        <div className="host-actions">
          <button className="primary-button compact" type="button" disabled={busy} onClick={() => void mutate((version) => draftsApi.formTeams(summary.id, null, version))}><Users /> Form solo teams</button>
        </div>
      )}

      <TeamRoster teams={detail.teams} nameOf={nameOf} onRemove={isHost && is2v2 ? removeTeam : undefined} busy={busy} />

      {canReady && me && (
        <div className="host-actions">
          <button className={`primary-button compact${me.isReady ? ' is-ready' : ''}`} type="button" disabled={busy} onClick={() => void mutate((version) => draftsApi.setReady(summary.id, !me.isReady, version))}>
            {me.isReady ? <><X /> I'm not ready</> : <><Check /> I'm ready</>}
          </button>
        </div>
      )}

      {isHost && (
        <div className="host-actions">
          <button className="primary-button compact" type="button" disabled={busy || !requirements.canBeginReadyCheck} onClick={() => void mutate((version) => draftsApi.beginReadyCheck(summary.id, version))}><Flag /> Begin ready check</button>
        </div>
      )}
      <RequirementSummary requirements={requirements} />
    </section>
  )
}
