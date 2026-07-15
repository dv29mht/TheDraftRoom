import { ArrowLeft, Check, Crown, DoorOpen, Lock, RefreshCw, Search, ShieldCheck, UserMinus, UserPlus, Users } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { draftsApi, getApiError } from '../services/api'
import { useAuthStore } from '../stores/authStore'
import type { DraftDetail, InvitableUser } from '../types/draft'

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
  const isOpen = summary?.status === 'Lobby'
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

      {!isOpen && (
        <div className="lobby-banner" role="status">
          <ShieldCheck aria-hidden="true" />
          <div><strong>Lobby locked</strong><span>Team formation, seeding, and the spinner arrive next — those controls are coming soon.</span></div>
        </div>
      )}

      <section className="panel lobby-attendance">
        <div className="panel-heading">
          <div><span className="eyebrow">Attendance</span><h3>{capacity.joinedCount} present · {capacity.participantCount} in lobby</h3></div>
          <Users aria-hidden="true" />
        </div>
        <div className="rule-summary">
          <span><strong>{capacity.participantCount}</strong>of {capacity.min}–{capacity.max}</span>
          <span><strong>{capacity.joinedCount}</strong>confirmed present</span>
          <span><strong>{capacity.requiresEven ? 'Even' : 'Any'}</strong>team parity</span>
        </div>
        <ul className="participant-list">
          {detail.participants.map((participant) => (
            <li key={participant.userId} className="participant-row">
              <span className="participant-avatar">{(participant.displayName ?? '?').slice(0, 2).toUpperCase()}</span>
              <div className="participant-identity">
                <strong>{participant.displayName ?? 'Unknown player'}{participant.userId === userId && <em> · You</em>}</strong>
                {participant.email && <small>{participant.email}</small>}
              </div>
              {participant.isHost && <span className="host-badge"><Crown aria-hidden="true" /> Host</span>}
              <span className={`status-pill ${participant.status === 'Joined' ? 'status-joined' : 'status-invited'}`}>{participant.status === 'Joined' ? 'Present' : 'Invited'}</span>
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

      {isHost && isOpen && (
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
            <button className="secondary-button" type="button" disabled title="Available once the lobby is locked (PR-12/PR-13)">Start draft · Coming soon</button>
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

      {error && <div className="form-error" role="alert">{error}</div>}
    </div>
  )
}

function lobbyStatusLabel(status: string): string {
  switch (status) {
    case 'Lobby': return 'Open lobby'
    case 'TeamFormation': return 'Team formation'
    case 'ReadyCheck': return 'Ready check'
    case 'Draft': return 'Draft'
    default: return status
  }
}
