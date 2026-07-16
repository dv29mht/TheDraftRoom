import { ArrowLeft, Ban, Check, Clock, Crown, DoorOpen, Flag, ListChecks, Lock, Pause, Play, RefreshCw, RotateCcw, Search, Shield, Shuffle, Star, Trophy, UserMinus, UserPlus, Users, WifiOff, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { SpinnerWheel } from '../components/SpinnerWheel'
import { draftsApi, getApiError } from '../services/api'
import { connectDraftHub } from '../services/draftHub'
import type { DraftHubStatus } from '../services/draftHub'
import { useAuthStore } from '../stores/authStore'
import type { DraftBoard, DraftDetail, DraftPick, DraftSeed, DraftTeam, DraftTimer, InvitableUser, LobbyParticipant, TeamFormationInput } from '../types/draft'

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
  const [board, setBoard] = useState<DraftBoard | null>(null)
  const [hubStatus, setHubStatus] = useState<DraftHubStatus>('connecting')

  const loadBoard = useCallback(async (clubId?: string) => {
    try {
      setBoard(await draftsApi.board(draftId, clubId))
    } catch {
      /* the board is only meaningful in the club/position stages; ignore elsewhere */
    }
  }, [draftId])

  // Accepts an authoritative snapshot from either channel (REST or hub push). Versions only move
  // forward, so an out-of-order push can never overwrite a newer state.
  const applySnapshot = useCallback((next: DraftDetail) => {
    setDetail((current) => current == null || next.summary.version >= current.summary.version ? next : current)
    setNotFound(false)
    if (next.summary.status === 'ClubSelection' || next.summary.status === 'PositionDraft') {
      void loadBoard()
    }
  }, [loadBoard])

  const load = useCallback(async () => {
    try {
      const next = await draftsApi.get(draftId)
      setDetail(next)
      setNotFound(false)
      if (next.summary.status === 'ClubSelection' || next.summary.status === 'PositionDraft') {
        await loadBoard()
      }
    } catch (requestError) {
      const status = (requestError as { response?: { status?: number } })?.response?.status
      if (status === 404) setNotFound(true)
      else setError(getApiError(requestError))
    } finally {
      setLoading(false)
    }
  }, [draftId, loadBoard])

  useEffect(() => { void load() }, [load])

  // Live synchronization (PR-17): join the draft's hub group and apply every pushed snapshot. SignalR
  // augments the REST reads — load()/refresh stay the fallback, and each (re)join reconciles from the
  // authoritative snapshot the server returns, so a reconnect never duplicates an action.
  useEffect(() => {
    if (!draftId) return
    const hub = connectDraftHub(draftId, {
      onUpdate: (update) => {
        if (update.detail) applySnapshot(update.detail)
        else void load() // envelope without a snapshot → refetch the authoritative state
      },
      onSnapshot: applySnapshot,
      onStatusChange: setHubStatus,
    })
    return () => { void hub.stop() }
  }, [draftId, applySnapshot, load])

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
      const next = await action(detail.summary.version)
      setDetail(next)
      if (next.summary.status === 'ClubSelection' || next.summary.status === 'PositionDraft') {
        await loadBoard()
      }
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
  const inClubSelection = status === 'ClubSelection'
  const inPositionDraft = status === 'PositionDraft'
  const isPaused = status === 'Paused'
  const isCompleted = status === 'Completed'
  const isCancelled = status === 'Cancelled' || status === 'Abandoned'
  const canReady = inTeamFormation || inReadyCheck
  const orderCommitted = detail.teams.length > 0 && detail.teams.every((team) => team.spinnerRank != null)

  const nameOf = (id: string) => detail.participants.find((participant) => participant.userId === id)?.displayName ?? 'Unknown player'

  const runSpinner = () => {
    setSpinning(true)
    void mutate((version) => draftsApi.commitSpinner(summary.id, version)).finally(() => setTimeout(() => setSpinning(false), 2200))
  }

  return (
    <div className="page lobby-page">
      <Link className="back-link" to="/drafts"><ArrowLeft /> Draft hub</Link>

      {hubStatus !== 'connected' && (
        <div className={`lobby-banner connection-banner connection-${hubStatus}`} role="status">
          <WifiOff aria-hidden="true" />
          <div>
            <strong>{hubStatus === 'reconnecting' ? 'Reconnecting…' : hubStatus === 'connecting' ? 'Connecting live updates…' : 'Live updates offline'}</strong>
            <span>{hubStatus === 'offline' ? 'Actions still work — refresh to sync manually.' : 'The lobby keeps working while we restore the live connection.'}</span>
          </div>
        </div>
      )}

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
          {orderCommitted && (
            <div className="lobby-banner" role="status">
              <ShieldOk />
              <div>
                <strong>Order committed</strong>
                <span>{isHost ? 'Open club selection to begin the pre-draft round in spinner order.' : 'Waiting for the host to open club selection.'}</span>
              </div>
            </div>
          )}
          {isHost && orderCommitted && (
            <div className="host-actions">
              <button className="primary-button compact" type="button" disabled={busy} onClick={() => void mutate((version) => draftsApi.openClubSelection(summary.id, version))}><Star /> Open club selection</button>
            </div>
          )}
        </section>
      )}

      {inClubSelection && (
        <ClubSelectionStage
          detail={detail}
          board={board}
          isHost={isHost}
          busy={busy}
          userId={userId}
          nameOf={nameOf}
          loadBoard={loadBoard}
          mutate={mutate}
        />
      )}

      {inPositionDraft && (
        <PositionDraftStage
          detail={detail}
          board={board}
          busy={busy}
          userId={userId}
          mutate={mutate}
        />
      )}

      {isPaused && (
        <PausedStage detail={detail} isHost={isHost} busy={busy} mutate={mutate} />
      )}

      {(inClubSelection || inPositionDraft) && isHost && (
        <HostDraftControls detail={detail} busy={busy} mutate={mutate} />
      )}

      {isCompleted && <CompletedStage detail={detail} />}

      {isCancelled && <CancelledStage detail={detail} />}

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

type StageMutate = (action: (version: number) => Promise<DraftDetail>) => Promise<void>

// The pre-draft five-star club + protected-player round (PR-14). Straight spinner order: the active team
// picks a five-star club, then a 75+ player from it. The pools come from the server board so the client never
// re-derives eligibility or turn order.
function ClubSelectionStage({ detail, board, isHost, busy, userId, nameOf, loadBoard, mutate }: {
  detail: DraftDetail
  board: DraftBoard | null
  isHost: boolean
  busy: boolean
  userId: string | undefined
  nameOf: (id: string) => string
  loadBoard: (clubId?: string) => Promise<void>
  mutate: StageMutate
}) {
  const summary = detail.summary
  const turn = detail.turn
  const [selectedClub, setSelectedClub] = useState('')
  const isMyTurn = board?.isMyTurn ?? (userId != null && turn.activeTeamMemberUserIds.includes(userId))
  const teams = [...detail.teams].sort((a, b) => (a.spinnerRank ?? 0) - (b.spinnerRank ?? 0))
  const allChosen = teams.length > 0 && teams.every((team) => team.selectedClubId != null)
  const heldOf = (teamId: string) => detail.picks.find((pick) => pick.teamId === teamId && pick.slotOrder === 0)

  const chooseClub = (clubId: string) => {
    setSelectedClub(clubId)
    void loadBoard(clubId) // fetch that club's still-available 75+ pool for the held pick
  }

  const protect = (footballerId: number) => {
    if (!selectedClub) return
    void mutate((version) => draftsApi.selectClubAndProtect(summary.id, selectedClub, footballerId, version)).then(() => setSelectedClub(''))
  }

  return (
    <section className="panel formation-panel">
      <div className="panel-heading"><div><span className="eyebrow">Club selection</span><h3>Five-star club &amp; protected player</h3></div><Star aria-hidden="true" /></div>

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
            <select value={selectedClub} onChange={(event) => chooseClub(event.target.value)} aria-label="Five-star club" disabled={busy}>
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
                  <button className="ghost-button" type="button" disabled={busy} onClick={() => protect(footballer.id)}><Shield /> Protect</button>
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
    </section>
  )
}

// The position draft (PR-15). Snake order over committed spinner ranks; the server board says whose turn it
// is and which slot/position is open, and lists the eligible, still-available pool for that slot.
function PositionDraftStage({ detail, board, busy, userId, mutate }: {
  detail: DraftDetail
  board: DraftBoard | null
  busy: boolean
  userId: string | undefined
  mutate: StageMutate
}) {
  const summary = detail.summary
  const turn = detail.turn
  const isMyTurn = board?.isMyTurn ?? (userId != null && turn.activeTeamMemberUserIds.includes(userId))

  const pick = (footballerId: number) => void mutate((version) => draftsApi.submitPick(summary.id, footballerId, version))

  return (
    <section className="panel formation-panel">
      <div className="panel-heading"><div><span className="eyebrow">Position draft</span><h3>On the clock</h3></div><ListChecks aria-hidden="true" /></div>

      <div className="lobby-banner" role="status">
        <ListChecks aria-hidden="true" />
        <div>
          <strong>{turn.activeTeamName ?? 'Draft'} · {turn.activeSlotLabel ?? 'Pick'}</strong>
          <span>
            Round {turn.round} ({turn.direction.toLowerCase()} order){turn.activeSlotPosition ? ` · needs a ${turn.activeSlotPosition}` : turn.slotAcceptsAnyPosition ? ' · any position' : ''}
          </span>
        </div>
        <TurnCountdown timer={detail.timer} />
      </div>

      {isMyTurn ? (
        <ul className="pick-pool" aria-label="Eligible players">
          {(board?.eligibleFootballers ?? []).length === 0 && <li className="invite-empty">No eligible players are available for this slot.</li>}
          {(board?.eligibleFootballers ?? []).map((footballer) => (
            <li key={footballer.id}>
              <div><strong>{footballer.name}</strong><small>{footballer.overall} · {footballer.positions.join('/')} · {footballer.clubName}</small></div>
              <button className="ghost-button" type="button" disabled={busy} onClick={() => pick(footballer.id)}><Check /> Draft</button>
            </li>
          ))}
        </ul>
      ) : (
        <p className="coming-soon-note" role="status">Waiting for {turn.activeTeamName ?? 'the active team'} to pick.</p>
      )}

      <SquadBoard detail={detail} />
    </section>
  )
}

// The live pick clock (PR-16/PR-17). The server's snapshot carries the authoritative deadline and its own
// measured remaining seconds; the client only ticks the display down between server updates, calibrated
// against the server measurement so local clock skew cannot change the result. Warning styling begins at
// the server's §6.4 threshold (15s); a paused clock renders frozen.
function TurnCountdown({ timer }: { timer: DraftTimer }) {
  const [remaining, setRemaining] = useState<number | null>(timer.remainingSeconds)

  useEffect(() => {
    setRemaining(timer.remainingSeconds)
    if (!timer.isTimed || timer.isPaused || timer.deadline == null) return
    const serverRemaining = timer.remainingSeconds ?? (new Date(timer.deadline).getTime() - Date.now()) / 1000
    const endAt = Date.now() + serverRemaining * 1000
    const tick = () => setRemaining(Math.max(0, (endAt - Date.now()) / 1000))
    tick()
    const interval = window.setInterval(tick, 500)
    return () => window.clearInterval(interval)
  }, [timer])

  if (!timer.isTimed || remaining == null) return null
  const seconds = Math.ceil(remaining)
  const warning = !timer.isPaused && seconds <= timer.warningSeconds
  const label = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
  return (
    <span
      className={`turn-countdown${warning ? ' is-warning' : ''}${timer.isPaused ? ' is-paused' : ''}`}
      role="timer"
      aria-label={timer.isPaused ? `Pick timer paused at ${label}` : `${seconds} seconds left to pick`}
    >
      <Clock aria-hidden="true" /> {timer.isPaused ? `Paused · ${label}` : label}
    </span>
  )
}

// Host pause/cancel with the required reason (PR-16, PRD §9.6). One shared reason field keeps the
// controls minimal; the server rejects a blank reason regardless.
function HostDraftControls({ detail, busy, mutate }: { detail: DraftDetail; busy: boolean; mutate: StageMutate }) {
  const summary = detail.summary
  const [reason, setReason] = useState('')
  const ready = reason.trim().length > 0

  return (
    <section className="panel host-controls">
      <div className="panel-heading"><div><span className="eyebrow">Host controls</span><h3>Pause or cancel</h3></div><Pause aria-hidden="true" /></div>
      <label className="control-reason">
        <span>Reason (required)</span>
        <input
          type="text"
          value={reason}
          maxLength={512}
          placeholder="Why are you pausing or cancelling?"
          onChange={(event) => setReason(event.target.value)}
          aria-label="Reason for pausing or cancelling"
          disabled={busy}
        />
      </label>
      <div className="host-actions">
        <button
          className="secondary-button"
          type="button"
          disabled={busy || !ready}
          onClick={() => void mutate((version) => draftsApi.pause(summary.id, reason.trim(), version)).then(() => setReason(''))}
        >
          <Pause /> Pause draft
        </button>
        <button
          className="ghost-button danger"
          type="button"
          disabled={busy || !ready}
          onClick={() => void mutate((version) => draftsApi.cancel(summary.id, reason.trim(), version)).then(() => setReason(''))}
        >
          <Ban /> Cancel draft
        </button>
      </div>
    </section>
  )
}

// The paused stage (PR-16). The clock is frozen server-side (paused time never elapses); the squads stay
// visible so everyone keeps their bearings, and the host resumes exactly where the draft left off.
function PausedStage({ detail, isHost, busy, mutate }: {
  detail: DraftDetail
  isHost: boolean
  busy: boolean
  mutate: StageMutate
}) {
  const summary = detail.summary
  const pauseReason = [...detail.events].reverse().find((evt) => evt.type === 'DraftPaused')?.reason

  return (
    <section className="panel formation-panel">
      <div className="panel-heading"><div><span className="eyebrow">Paused</span><h3>The draft is paused</h3></div><Pause aria-hidden="true" /></div>
      <div className="lobby-banner" role="status">
        <Pause aria-hidden="true" />
        <div>
          <strong>Draft paused{pauseReason ? ` — ${pauseReason}` : ''}</strong>
          <span>The pick clock is frozen; paused time never counts against the turn.</span>
        </div>
        <TurnCountdown timer={detail.timer} />
      </div>
      {isHost && (
        <div className="host-actions">
          <button className="primary-button compact" type="button" disabled={busy} onClick={() => void mutate((version) => draftsApi.resume(summary.id, version))}>
            <Play /> Resume draft
          </button>
        </div>
      )}
      {!isHost && <p className="coming-soon-note" role="status">Waiting for the host to resume.</p>}
      <SquadBoard detail={detail} />
    </section>
  )
}

// The terminal cancelled/abandoned stage (PR-16): history is preserved, nothing more can happen.
function CancelledStage({ detail }: { detail: DraftDetail }) {
  const cancelReason = [...detail.events].reverse()
    .find((evt) => evt.type === 'DraftCancelled' || evt.type === 'DraftAbandoned')?.reason

  return (
    <section className="panel formation-panel">
      <div className="panel-heading"><div><span className="eyebrow">{detail.summary.status}</span><h3>This draft has ended</h3></div><Ban aria-hidden="true" /></div>
      <div className="lobby-banner" role="status">
        <Ban aria-hidden="true" />
        <div>
          <strong>Draft {detail.summary.status.toLowerCase()}{cancelReason ? ` — ${cancelReason}` : ''}</strong>
          <span>The full history is preserved; no further picks or controls are possible.</span>
        </div>
      </div>
      <SquadBoard detail={detail} />
    </section>
  )
}

function CompletedStage({ detail }: { detail: DraftDetail }) {
  return (
    <section className="panel formation-panel">
      <div className="panel-heading"><div><span className="eyebrow">Draft complete</span><h3>Final squads</h3></div><Trophy aria-hidden="true" /></div>
      <div className="lobby-banner" role="status">
        <Trophy aria-hidden="true" />
        <div><strong>Draft complete</strong><span>Every team has filled all 16 squad slots.</span></div>
      </div>
      <SquadBoard detail={detail} />
    </section>
  )
}

// A per-team squad view driven by the frozen roster slots and the accepted picks — the held player then each
// slot in order, filled or still open. Shared by the position-draft and completed stages.
function SquadBoard({ detail }: { detail: DraftDetail }) {
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

function lobbyStatusLabel(status: string): string {
  switch (status) {
    case 'Lobby': return 'Open lobby'
    case 'TeamFormation': return 'Team formation'
    case 'ReadyCheck': return 'Ready check'
    case 'SpinnerRanking': return 'Spinner ranking'
    case 'ClubSelection': return 'Club selection'
    case 'PositionDraft': return 'Position draft'
    case 'Paused': return 'Paused'
    case 'Completed': return 'Completed'
    case 'Cancelled': return 'Cancelled'
    case 'Abandoned': return 'Abandoned'
    case 'Draft': return 'Draft'
    default: return status
  }
}
