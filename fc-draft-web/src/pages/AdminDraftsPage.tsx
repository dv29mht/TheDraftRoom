import { CalendarClock, DraftingCompass, ExternalLink, Eye, Pause, Play, Plus, Radio, RefreshCw, Users, UsersRound, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { draftsApi, getApiError, isApiConflict } from '../services/api'
import type { DraftDetail, DraftSummary } from '../types/draft'
import { ErrorBanner, LoadingState, SuccessBanner } from '../components/ui/Feedback'
import { Modal } from '../components/ui/Modal'
import { StatusPill } from '../components/ui/StatusPill'
import { useAnnouncer } from '../hooks/useAnnouncer'

type DraftOperation = 'pause' | 'resume' | 'cancel'

const PAUSABLE = new Set(['ClubSelection', 'PositionDraft'])
const TERMINAL = new Set(['Completed', 'Cancelled', 'Abandoned'])

/**
 * Admin draft operations (PR-21, §8.2): the read-only list became real operations — inspect any
 * draft's state, participants, and append-only event history, and pause/resume/cancel it on the
 * existing PR-16 commands. Pause and cancel capture a required reason; every action carries the
 * last-seen version, so a stale command surfaces as a 409 and the snapshot resyncs.
 */
export function AdminDraftsPage() {
  const [drafts, setDrafts] = useState<DraftSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const [inspected, setInspected] = useState<DraftDetail | null>(null)
  const [inspectLoading, setInspectLoading] = useState(false)
  const [operation, setOperation] = useState<DraftOperation | null>(null)
  const [reason, setReason] = useState('')
  const [operationBusy, setOperationBusy] = useState(false)
  const [operationError, setOperationError] = useState('')
  const { announce, announcer } = useAnnouncer()

  const loadList = async () => {
    const next = await draftsApi.list()
    setDrafts(next)
  }

  useEffect(() => {
    let active = true
    draftsApi.list()
      .then((next) => { if (active) setDrafts(next) })
      .catch((requestError) => { if (active) setError(getApiError(requestError)) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])

  const inspect = async (draftId: string) => {
    setError('')
    setNotice('')
    setOperation(null)
    setReason('')
    setOperationError('')
    setInspectLoading(true)
    try {
      setInspected(await draftsApi.get(draftId))
    } catch (requestError) {
      setError(getApiError(requestError))
    } finally {
      setInspectLoading(false)
    }
  }

  const closeInspect = () => {
    if (operationBusy) return
    setInspected(null)
    setOperation(null)
    setReason('')
    setOperationError('')
  }

  const runOperation = async () => {
    if (!inspected || !operation) return
    const summary = inspected.summary
    setOperationBusy(true)
    setOperationError('')
    try {
      const updated = operation === 'pause'
        ? await draftsApi.pause(summary.id, reason.trim(), summary.version)
        : operation === 'resume'
          ? await draftsApi.resume(summary.id, summary.version)
          : await draftsApi.cancel(summary.id, reason.trim(), summary.version)
      setInspected(updated)
      setOperation(null)
      setReason('')
      const verb = operation === 'pause' ? 'paused' : operation === 'resume' ? 'resumed' : 'cancelled'
      setNotice(`“${summary.name}” was ${verb}.`)
      announce(`Draft ${verb}.`)
      await loadList()
    } catch (requestError) {
      if (isApiConflict(requestError)) {
        // Stale version: someone (or the timer) acted first. Resync so the next try uses the truth.
        setOperationError('The draft moved on before this action landed — the snapshot has been refreshed. Review it and try again.')
        try { setInspected(await draftsApi.get(summary.id)) } catch { /* keep the stale view */ }
        await loadList().catch(() => { /* list refresh is best-effort */ })
      } else {
        setOperationError(getApiError(requestError))
      }
    } finally {
      setOperationBusy(false)
    }
  }

  const oneVOne = drafts.filter((draft) => draft.format === '1v1').length
  const twoVTwo = drafts.filter((draft) => draft.format === '2v2').length

  const status = inspected?.summary.status ?? ''
  const canPause = PAUSABLE.has(status)
  const canResume = status === 'Paused'
  const canCancel = !TERMINAL.has(status) && status !== ''
  const reasonRequired = operation === 'pause' || operation === 'cancel'
  const confirmDisabled = operationBusy || (reasonRequired && reason.trim() === '')

  const participantName = (userId: string | null) => {
    if (!userId) return 'System'
    const participant = inspected?.participants.find((candidate) => candidate.userId === userId)
    return participant?.displayName ?? participant?.email ?? `${userId.slice(0, 8)}…`
  }

  return (
    <div className="page">
      <h1 className="sr-only">Draft operations</h1>
      {announcer}
      <section className="stat-grid">
        <article><span className="stat-icon primary"><DraftingCompass /></span><div><strong>{drafts.length}</strong><small>Total drafts</small></div></article>
        <article><span className="stat-icon accent"><UsersRound /></span><div><strong>{oneVOne}</strong><small>1v1 drafts</small></div></article>
        <article><span className="stat-icon gold"><Radio /></span><div><strong>{twoVTwo}</strong><small>2v2 drafts</small></div></article>
      </section>

      {error && <ErrorBanner>{error}</ErrorBanner>}
      {notice && <SuccessBanner onDismiss={() => setNotice('')}>{notice}</SuccessBanner>}
      <section className="panel admin-module-panel">
        <div className="directory-toolbar">
          <div><span className="eyebrow">Operations</span><h2>All drafts</h2></div>
          <Link className="primary-button compact" to="/drafts/new"><Plus /> Create lobby</Link>
        </div>
        {loading ? <LoadingState>Loading drafts…</LoadingState> : drafts.length ? (
          <div className="admin-card-list">
            {drafts.map((draft) => (
              <div className="admin-list-card admin-draft-card" key={draft.id}>
                <span className="admin-list-icon"><DraftingCompass /></span>
                <div><strong>{draft.name}</strong><small><Users aria-hidden="true" /> {draft.participantCount} in lobby</small></div>
                <span className="card-badges"><span className="format-badge">{draft.format}</span><StatusPill status={draft.status} /></span>
                <code>{draft.code}</code>
                <time dateTime={draft.createdAt}><CalendarClock /> {new Date(draft.createdAt).toLocaleDateString()}</time>
                <div className="admin-draft-actions">
                  <button
                    className="secondary-button"
                    type="button"
                    disabled={inspectLoading}
                    onClick={() => void inspect(draft.id)}
                    aria-label={`Inspect ${draft.name}`}
                  >
                    <Eye /> Inspect
                  </button>
                  <Link className="secondary-button" to={`/drafts/${draft.id}`} aria-label={`Open ${draft.name}`}>
                    <ExternalLink /> Open
                  </Link>
                </div>
              </div>
            ))}
          </div>
        ) : <div className="empty-list"><DraftingCompass /><strong>No drafts yet</strong><span>Create the first lobby to begin tracking operations.</span><Link className="secondary-button" to="/drafts/new">Create a lobby</Link></div>}
      </section>

      {inspected && (
        <Modal onClose={closeInspect} labelledBy="inspect-draft-title" dialogClassName="confirm-dialog inspect-dialog">
          <div className="inspect-dialog-heading">
            <div>
              <span className="eyebrow">{inspected.summary.code} · v{inspected.summary.version}</span>
              <h2 id="inspect-draft-title">{inspected.summary.name}</h2>
            </div>
            <span className="card-badges">
              <span className="format-badge">{inspected.summary.format}</span>
              <StatusPill status={inspected.summary.status} />
            </span>
            <button className="icon-button" type="button" onClick={closeInspect} aria-label="Close draft inspection"><X /></button>
          </div>

          {operationError && <ErrorBanner>{operationError}</ErrorBanner>}

          <section className="inspect-section" aria-labelledby="inspect-participants-title">
            <h3 id="inspect-participants-title">Participants ({inspected.participants.length})</h3>
            <ul className="inspect-participants">
              {inspected.participants.map((participant) => (
                <li key={participant.userId}>
                  <strong>{participant.displayName ?? participant.email ?? 'Unknown account'}</strong>
                  <span>
                    {participant.isHost && 'Host · '}
                    {participant.seed ? `${participant.seed} · ` : ''}
                    {participant.status}{participant.isReady ? ' · Ready' : ''}
                  </span>
                </li>
              ))}
            </ul>
          </section>

          <section className="inspect-section" aria-labelledby="inspect-operations-title">
            <h3 id="inspect-operations-title">Operations</h3>
            {operation === null ? (
              <div className="inspect-operations">
                <button className="secondary-button" type="button" disabled={!canPause} onClick={() => { setOperation('pause'); setOperationError('') }}>
                  <Pause /> Pause…
                </button>
                <button className="secondary-button" type="button" disabled={!canResume} onClick={() => { setOperation('resume'); setOperationError('') }}>
                  <Play /> Resume…
                </button>
                <button className="danger-button" type="button" disabled={!canCancel} onClick={() => { setOperation('cancel'); setOperationError('') }}>
                  <X /> Cancel draft…
                </button>
                {TERMINAL.has(status) && <p className="field-hint">This draft is {status.toLowerCase()} — its history below is final.</p>}
              </div>
            ) : (
              <form className="inspect-operation-form" onSubmit={(event) => { event.preventDefault(); void runOperation() }}>
                <p>
                  {operation === 'pause' && 'Pausing freezes the pick clock for everyone until an admin or the host resumes.'}
                  {operation === 'resume' && `Resuming returns the draft to the round it paused from; paused time never elapsed.`}
                  {operation === 'cancel' && 'Cancelling ends this draft for every participant and notifies them with your reason. The history is preserved.'}
                </p>
                <label className="field" htmlFor="operation-reason">
                  <span className="field-label">Reason {reasonRequired ? '(required — participants and the audit trail see it)' : '(optional)'}</span>
                  <textarea
                    id="operation-reason"
                    rows={2}
                    maxLength={512}
                    required={reasonRequired}
                    value={reason}
                    onChange={(event) => setReason(event.target.value)}
                    placeholder={operation === 'cancel' ? 'e.g. Restarting after a rules dispute' : 'e.g. Connection problems on Team 2'}
                  />
                </label>
                <div className="confirm-actions">
                  <button className="secondary-button" type="button" disabled={operationBusy} onClick={() => { setOperation(null); setReason('') }}>
                    Back
                  </button>
                  <button
                    className={operation === 'cancel' ? 'danger-button confirm-delete' : 'primary-button'}
                    type="submit"
                    disabled={confirmDisabled}
                  >
                    {operationBusy ? <RefreshCw className="spin" /> : null}
                    {operationBusy ? 'Applying…' : operation === 'pause' ? 'Confirm pause' : operation === 'resume' ? 'Confirm resume' : 'Confirm cancellation'}
                  </button>
                </div>
              </form>
            )}
          </section>

          <section className="inspect-section" aria-labelledby="inspect-history-title">
            <h3 id="inspect-history-title">Event history ({inspected.events.length})</h3>
            <div className="table-scroll inspect-history-scroll">
              <table className="users-table inspect-history-table">
                <thead>
                  <tr><th>#</th><th>Event</th><th>Transition</th><th>Actor</th><th>Reason</th><th>When</th></tr>
                </thead>
                <tbody>
                  {[...inspected.events].sort((a, b) => b.sequence - a.sequence).map((event) => (
                    <tr key={event.sequence}>
                      <td data-label="#">{event.sequence}</td>
                      <td data-label="Event">{event.type}</td>
                      <td data-label="Transition">{event.fromStatus ? `${event.fromStatus} → ${event.toStatus ?? '—'}` : event.toStatus ?? '—'}</td>
                      <td data-label="Actor">{participantName(event.actorUserId)}</td>
                      <td data-label="Reason">{event.reason ?? '—'}</td>
                      <td data-label="When"><time dateTime={event.createdAt}>{new Date(event.createdAt).toLocaleString()}</time></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </Modal>
      )}
    </div>
  )
}
