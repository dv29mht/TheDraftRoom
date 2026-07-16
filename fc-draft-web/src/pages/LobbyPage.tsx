import { ArrowLeft, Check, Crown, DoorOpen, Flag, Lock, Play, RefreshCw, RotateCcw, Search, Shuffle, UserMinus, UserPlus, Users, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { SpinnerWheel } from '../components/SpinnerWheel'
import { draftsApi, getApiError } from '../services/api'
import { useAuthStore } from '../stores/authStore'
import type { DraftDetail, DraftSeed, DraftTeam, InvitableUser, LobbyParticipant, TeamFormationInput } from '../types/draft'

export function LobbyPage() {
  const { draftId = '' } = useParams()
  const userId = useAuthStore((state) => state.user?.id)
  const [detail, setDetail] = useState<DraftDetail | null>(null)
  const [candidates, setCandidates] = useState<InvitableUser[]>([])
  const [loading, setLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [search, setSearch] = useState('')
  const [spinning, setSpinning] = useState(false)

  const load = useCallback(async () => {
    try {
      setDetail(await draftsApi.get(draftId))
      setNotFound(false)
    } catch (requestError) {
      const status = (requestError as { response?: { status?: number } })?.response?.status
      if (status === 404) setNotFound(true)
      else setError(getApiError(requestError))
    } finally {
      setLoading(false)
    }
  }, [draftId])

  useEffect(() => { void load() }, [load])

  const summary = detail?.summary
  const isHost = !!summary && summary.hostUserId === userId
  const me = detail?.participants.find((participant) => participant.userId === userId)
  const status = summary?.status
  const isOpen = status === 'Lobby'
  const participantIds = useMemo(
    () => new Set(detail?.participants.map((participant) => participant.userId) ?? []),
    [detail]
  )

  useEffect(() => {
    if (!isHost || !isOpen) return
    let active = true
    void draftsApi.invitableUsers().then((users) => { if (active) setCandidates(users) }).catch(() => { /* non-fatal */ })
    return () => { active = false }
  }, [isHost, isOpen])

  const mutate = async (action: (version: number) => Promise<DraftDetail>) => {
    if (!detail) return
    setBusy(true)
    setError('')
    try {
      setDetail(await action(detail.summary.version))
    } catch (requestError) {
      setError(getApiError(requestError))
      await load() // resync the version so a follow-up action uses the latest snapshot
    } finally {
      setBusy(false)
    }
  }

  const inviteCandidates = useMemo(() => {
    const term = search.trim().toLowerCase()
    return candidates
      .filter((user) => !participantIds.has(user.id))
      .filter((user) => !term || user.displayName.toLowerCase().includes(term) || user.email.toLowerCase().includes(term))
      .slice(0, 6)
  }, [candidates, participantIds, search])

  if (loading) return <div className="page"><div className="loading-state"><RefreshCw className="spin" /> Loading lobby…</div></div>
  if (notFound) return (
    <div className="page">
      <section className="panel placeholder-panel">
        <span className="empty-orb"><DoorOpen /></span>
        <span className="eyebrow">Lobby</span>
        <h2>Lobby unavailable</h2>
        <p>This lobby does not exist, or you are not one of its participants.</p>
        <Link to="/drafts">Back to draft hub</Link>
      </section>
    </div>
  )
  if (!detail || !summary) return null

  const capacity = detail.capacity
  const requirements = detail.startRequirements
  const inTeamFormation = status === 'TeamFormation'
  const inReadyCheck = status === 'ReadyCheck'
  const inSpinner = status === 'SpinnerRanking'
  const canReady = inTeamFormation || inReadyCheck

  const nameOf = (id: string) => detail.participants.find((participant) => participant.userId === id)?.displayName ?? 'Unknown player'

  const runSpinner = () => {
    setSpinning(true)
    void mutate((version) => draftsApi.commitSpinner(summary.id, version)).finally(() => setTimeout(() => setSpinning(false), 2200))
  }

  return (
    <div className="page lobby-page">
      <Link className="back-link" to="/drafts"><ArrowLeft /> Draft hub</Link>

      <section className="panel lobby-header">
        <div className="lobby-title">
          <span className="eyebrow">{summary.format} lobby</span>
          <h1>{summary.name}</h1>
          <div className="lobby-meta">
            <span className={`status-pill status-${summary.status.toLowerCase()}`}>{lobbyStatusLabel(summary.status)}</span>
            <span className="lobby-code">Code <b>{summary.code}</b></span>
          </div>
        </div>
        <button className="icon-button" type="button" onClick={() => void load()} aria-label="Refresh lobby" title="Refresh"><RefreshCw className={busy ? 'spin' : ''} /></button>
      </section>

      <section className="panel lobby-attendance">
        <div className="panel-heading">
          <div><span className="eyebrow">Attendance</span><h3>{capacity.joinedCount} present · {capacity.participantCount} in lobby</h3></div>
          <Users aria-hidden="true" />
        </div>
        <div className="rule-summary">
          <span><strong>{capacity.participantCount}</strong>of {capacity.min}–{capacity.max}</span>
          <span><strong>{capacity.joinedCount}</strong>confirmed present</span>
          <span><strong>{requirements.teamCount}</strong>teams formed</span>
        </div>
        <ul className="participant-list">
          {detail.participants.map((participant) => (
            <li key={participant.userId} className="participant-row">
              <span className="participant-avatar">{(participant.displayName ?? '?').slice(0, 2).toUpperCase()}</span>
              <div className="participant-identity">
                <strong>{participant.displayName ?? 'Unknown player'}{participant.userId === userId && <em> · You</em>}</strong>
                {participant.email && <small>{participant.email}</small>}
              </div>
              {participant.seed && <span className={`seed-chip seed-${participant.seed.toLowerCase()}`}>{participant.seed === 'Seed1' ? 'Seed 1' : 'Seed 2'}</span>}
              {participant.isHost && <span className="host-badge"><Crown aria-hidden="true" /> Host</span>}
              {!isOpen
                ? <span className={`status-pill ${participant.isReady ? 'status-joined' : 'status-invited'}`}>{participant.isReady ? 'Ready' : 'Not ready'}</span>
                : <span className={`status-pill ${participant.status === 'Joined' ? 'status-joined' : 'status-invited'}`}>{participant.status === 'Joined' ? 'Present' : 'Invited'}</span>}
              {isHost && isOpen && !participant.isHost && (
                <button className="ghost-button danger" type="button" disabled={busy} onClick={() => void mutate((version) => draftsApi.removeParticipant(summary.id, participant.userId, version))} aria-label={`Remove ${participant.displayName ?? 'participant'}`}>
                  <UserMinus />
                </button>
              )}
            </li>
          ))}
        </ul>
      </section>

      {isOpen && me && me.status === 'Invited' && (
        <section className="panel lobby-join-panel">
          <div><strong>You're invited to this lobby</strong><p>Confirm you're here so the host can start on time.</p></div>
          <button className="primary-button compact" type="button" disabled={busy} onClick={() => void mutate((version) => draftsApi.join(summary.id, version))}><Check /> Confirm presence</button>
        </section>
      )}

      {isOpen && isHost && (
        <section className="panel host-controls">
          <div className="panel-heading"><div><span className="eyebrow">Host controls</span><h3>Manage this lobby</h3></div></div>

          <div className="invite-search">
            <Search aria-hidden="true" />
            <input type="search" placeholder="Search players to invite" value={search} onChange={(event) => setSearch(event.target.value)} aria-label="Search players to invite" />
          </div>
          <ul className="invite-candidate-list" aria-label="People you can invite">
            {inviteCandidates.length === 0 && <li className="invite-empty">{candidates.length === 0 ? 'No other active players to invite.' : 'No matches.'}</li>}
            {inviteCandidates.map((user) => (
              <li key={user.id}>
                <div><strong>{user.displayName}</strong><small>{user.email}</small></div>
                <button className="ghost-button" type="button" disabled={busy || capacity.participantCount >= capacity.max} onClick={() => void mutate((version) => draftsApi.invite(summary.id, user.id, version))}><UserPlus /> Invite</button>
              </li>
            ))}
          </ul>

          <div className="host-actions">
            <button className="primary-button compact" type="button" disabled={busy || !capacity.canLock} onClick={() => void mutate((version) => draftsApi.lock(summary.id, version))}>
              <Lock /> Lock lobby &amp; continue
            </button>
          </div>
          {!capacity.canLock && (
            <p className="coming-soon-note">
              {!capacity.meetsMinimum
                ? `Invite at least ${capacity.min - capacity.participantCount} more to reach the ${summary.format} minimum of ${capacity.min}.`
                : !capacity.meetsEven
                  ? 'A 2v2 lobby needs an even number of participants.'
                  : 'This lobby cannot be locked yet.'}
            </p>
          )}
        </section>
      )}

      {inTeamFormation && (
        <TeamFormationPanel
          detail={detail}
          isHost={isHost}
          busy={busy}
          canReady={canReady}
          me={me}
          nameOf={nameOf}
          mutate={mutate}
        />
      )}

      {inReadyCheck && (
        <section className="panel formation-panel">
          <div className="panel-heading"><div><span className="eyebrow">Ready check</span><h3>Confirm and start</h3></div><Flag aria-hidden="true" /></div>
          <TeamRoster teams={detail.teams} nameOf={nameOf} />
          {me && (
            <div className="host-actions">
              <button className={`primary-button compact${me.isReady ? ' is-ready' : ''}`} type="button" disabled={busy} onClick={() => void mutate((version) => draftsApi.setReady(summary.id, !me.isReady, version))}>
                {me.isReady ? <><X /> I'm not ready</> : <><Check /> I'm ready</>}
              </button>
            </div>
          )}
          {isHost && (
            <div className="host-actions">
              <button className="secondary-button" type="button" disabled={busy} onClick={() => void mutate((version) => draftsApi.reopenTeams(summary.id, version))}><RotateCcw /> Reopen teams</button>
              <button className="primary-button compact" type="button" disabled={busy || !requirements.canStart} onClick={() => void mutate((version) => draftsApi.start(summary.id, version))}><Play /> Start draft</button>
            </div>
          )}
          <RequirementSummary requirements={requirements} />
        </section>
      )}

      {inSpinner && (
        <section className="panel formation-panel">
          <div className="panel-heading"><div><span className="eyebrow">Spinner ranking</span><h3>Team order</h3></div><Shuffle aria-hidden="true" /></div>
          <p className="coming-soon-note">The order is decided on the server with an unbiased shuffle. The wheel only reveals the result.</p>
          <SpinnerWheel teams={detail.teams} spinning={spinning} />
          {isHost && !detail.teams.every((team) => team.spinnerRank != null) && (
            <div className="host-actions">
              <button className="primary-button compact" type="button" disabled={busy || spinning} onClick={runSpinner}><Shuffle /> Spin the wheel</button>
            </div>
          )}
          {detail.teams.every((team) => team.spinnerRank != null) && (
            <div className="lobby-banner" role="status">
              <ShieldOk />
              <div><strong>Order committed</strong><span>Club selection arrives next — that step is coming soon.</span></div>
            </div>
          )}
        </section>
      )}

      {error && <div className="form-error" role="alert">{error}</div>}
    </div>
  )
}

function TeamFormationPanel({ detail, isHost, busy, canReady, me, nameOf, mutate }: {
  detail: DraftDetail
  isHost: boolean
  busy: boolean
  canReady: boolean
  me: LobbyParticipant | undefined
  nameOf: (id: string) => string
  mutate: (action: (version: number) => Promise<DraftDetail>) => Promise<void>
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
      <div className="panel-heading"><div><span className="eyebrow">Team formation</span><h3>{is2v2 ? 'Seed and pair teams' : 'Confirm solo teams'}</h3></div><Users aria-hidden="true" /></div>

      {is2v2 && isHost && (
        <div className="seed-assignment">
          <p className="step-label">Assign each player Seed 1 or Seed 2, then pair them into teams.</p>
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

function TeamRoster({ teams, nameOf, onRemove, busy }: {
  teams: DraftTeam[]
  nameOf: (id: string) => string
  onRemove?: (teamId: string) => void
  busy?: boolean
}) {
  if (teams.length === 0) return <p className="coming-soon-note">No teams formed yet.</p>
  return (
    <ul className="team-roster" aria-label="Formed teams">
      {teams.map((team) => (
        <li key={team.id} className="team-card">
          <div className="team-card-head">
            {team.spinnerRank != null && <span className="spinner-rank">{team.spinnerRank}</span>}
            <strong>{team.name}</strong>
            {onRemove && <button className="ghost-button danger" type="button" disabled={busy} onClick={() => onRemove(team.id)} aria-label={`Remove ${team.name}`}><X /></button>}
          </div>
          <div className="team-members">{team.memberUserIds.map(nameOf).join(' · ')}</div>
        </li>
      ))}
    </ul>
  )
}

function RequirementSummary({ requirements }: { requirements: DraftDetail['startRequirements'] }) {
  if (requirements.canStart || requirements.blockingReasons.length === 0) return null
  return (
    <ul className="requirement-summary" aria-label="What is still needed">
      {requirements.blockingReasons.map((reason) => <li key={reason}>{reason}</li>)}
    </ul>
  )
}

function ShieldOk() {
  return <Check aria-hidden="true" />
}

function lobbyStatusLabel(status: string): string {
  switch (status) {
    case 'Lobby': return 'Open lobby'
    case 'TeamFormation': return 'Team formation'
    case 'ReadyCheck': return 'Ready check'
    case 'SpinnerRanking': return 'Spinner ranking'
    case 'Draft': return 'Draft'
    default: return status
  }
}
