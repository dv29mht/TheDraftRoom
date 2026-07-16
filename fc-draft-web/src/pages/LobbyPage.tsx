import { ArrowLeft, Check, Crown, DoorOpen, Flag, Lock, Play, RefreshCw, RotateCcw, Search, Shuffle, Star, UserMinus, UserPlus, Users, WifiOff, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ClubSelectionStage } from '../components/draft/ClubSelectionStage'
import { DraftRoomStage } from '../components/draft/DraftRoomStage'
import { HostDraftControls } from '../components/draft/HostDraftControls'
import { PausedStage } from '../components/draft/PausedStage'
import { TeamFormationPanel } from '../components/draft/TeamFormationPanel'
import { RequirementSummary, TeamRoster } from '../components/draft/TeamRoster'
import { CancelledStage, CompletedStage } from '../components/draft/TerminalStages'
import { SpinnerWheel } from '../components/SpinnerWheel'
import { draftsApi, getApiError } from '../services/api'
import { connectDraftHub } from '../services/draftHub'
import type { DraftHubStatus } from '../services/draftHub'
import { useAuthStore } from '../stores/authStore'
import type { DraftDetail, InvitableUser } from '../types/draft'

// The lobby/draft orchestrator: loads the authoritative snapshot, keeps it live over the draft hub
// (PR-17), and renders the stage components (PR-18 extracted them — the draft-room experience itself
// lives in components/draft/). All mutations flow through mutate() so the optimistic version, error
// surface, and refresh-on-conflict behavior stay in one place.
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
  const [hubStatus, setHubStatus] = useState<DraftHubStatus>('connecting')

  // Accepts an authoritative snapshot from either channel (REST or hub push). Versions only move
  // forward, so an out-of-order push can never overwrite a newer state. Stages that need the board
  // refetch it themselves when the version moves.
  const applySnapshot = useCallback((next: DraftDetail) => {
    setDetail((current) => current == null || next.summary.version >= current.summary.version ? next : current)
    setNotFound(false)
  }, [])

  const load = useCallback(async () => {
    try {
      const next = await draftsApi.get(draftId)
      setDetail(next)
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
    } catch (requestError) {
      // Stale-command recovery (§17.7): a 409 means someone acted first — say so plainly, then resync
      // the snapshot so the very next action uses the latest version and pools.
      const status = (requestError as { response?: { status?: number } })?.response?.status
      setError(status === 409
        ? 'Someone else acted first — the draft has moved on. Everything has been refreshed; check the board and try again.'
        : getApiError(requestError))
      await load()
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

      {!inPositionDraft && (
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
      )}

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
              <Check aria-hidden="true" />
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
          isHost={isHost}
          busy={busy}
          userId={userId}
          nameOf={nameOf}
          mutate={mutate}
        />
      )}

      {inPositionDraft && (
        <DraftRoomStage
          detail={detail}
          busy={busy}
          userId={userId}
          hubStatus={hubStatus}
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
