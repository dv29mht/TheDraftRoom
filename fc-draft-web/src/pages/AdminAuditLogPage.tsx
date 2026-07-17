import { ScrollText, Search, ShieldCheck } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { auditApi, draftsApi, getApiError, usersApi } from '../services/api'
import type { DraftAuditEvent, ManagedUser, SecurityAuditEvent } from '../types/admin'
import type { DraftSummary } from '../types/draft'
import { ErrorBanner, LoadingState } from '../components/ui/Feedback'

const DRAFT_EVENT_TYPES = [
  'DraftCreated', 'ParticipantInvited', 'ParticipantJoined', 'ParticipantSeedAssigned', 'TeamsFormed',
  'ParticipantReadied', 'DraftStarted', 'SpinnerOrderCommitted', 'SpinnerOrderRevealed', 'ClubSelected',
  'FootballerProtected', 'PositionRoundStarted', 'PickAccepted', 'DraftPaused', 'DraftResumed',
  'DraftCompleted', 'DraftCancelled', 'DraftAbandoned', 'AdminRecoveryApplied', 'ParticipantRemoved',
  'LobbyLocked', 'ReadyCheckStarted', 'TeamFormationReopened', 'ClubSelectionStarted', 'PickAutoSelected',
]

const SECURITY_ACTIONS = [
  'SignInSucceeded', 'SignInFailed', 'SignInLockedOut', 'PasswordChanged', 'PasswordResetRequested',
  'PasswordReset', 'SessionsRevoked', 'AccountActivated', 'AccountDeactivated', 'UserCreated',
  'UserUpdated', 'UserInvited', 'DatasetImported', 'DatasetActivated', 'TemplateCreated',
  'TemplateActivated', 'ClubFiveStarChanged', 'AnnouncementSent',
]

/** date input (YYYY-MM-DD) → inclusive ISO range bound. */
const fromIso = (value: string) => (value ? new Date(`${value}T00:00:00`).toISOString() : undefined)
const toIso = (value: string) => (value ? new Date(`${value}T23:59:59.999`).toISOString() : undefined)

/**
 * The admin Audit Log module (PR-21, §9.10): immutable, filterable views over the two append-only
 * trails — every draft's lifecycle/pick events (with actor attribution and recorded reasons), and the
 * security/admin action trail. Strictly read-only: nothing on this page (or its API) can edit or
 * delete a record; draft corrections appear as appended compensating events.
 */
export function AdminAuditLogPage() {
  const [drafts, setDrafts] = useState<DraftSummary[]>([])
  const [users, setUsers] = useState<ManagedUser[]>([])
  const [error, setError] = useState('')

  const [draftFilter, setDraftFilter] = useState('')
  const [typeFilter, setTypeFilter] = useState('')
  const [actorFilter, setActorFilter] = useState('')
  const [draftFrom, setDraftFrom] = useState('')
  const [draftTo, setDraftTo] = useState('')
  const [draftEvents, setDraftEvents] = useState<DraftAuditEvent[]>([])
  const [draftEventsLoading, setDraftEventsLoading] = useState(true)

  const [actionFilter, setActionFilter] = useState('')
  const [emailFilter, setEmailFilter] = useState('')
  const [securityFrom, setSecurityFrom] = useState('')
  const [securityTo, setSecurityTo] = useState('')
  const [securityEvents, setSecurityEvents] = useState<SecurityAuditEvent[]>([])
  const [securityLoading, setSecurityLoading] = useState(true)

  useEffect(() => {
    let active = true
    Promise.all([draftsApi.list(), usersApi.list({ page: 1, pageSize: 50 })])
      .then(([allDrafts, directory]) => {
        if (!active) return
        setDrafts(allDrafts)
        setUsers(directory.items)
      })
      .catch((requestError) => { if (active) setError(getApiError(requestError)) })
    return () => { active = false }
  }, [])

  const loadDraftEvents = useCallback(async () => {
    setDraftEventsLoading(true)
    try {
      setDraftEvents(await auditApi.draftEvents({
        draftId: draftFilter || undefined,
        type: typeFilter || undefined,
        actorUserId: actorFilter || undefined,
        from: fromIso(draftFrom),
        to: toIso(draftTo),
      }))
      setError('')
    } catch (requestError) {
      setError(getApiError(requestError))
    } finally {
      setDraftEventsLoading(false)
    }
  }, [draftFilter, typeFilter, actorFilter, draftFrom, draftTo])

  const loadSecurityEvents = useCallback(async () => {
    setSecurityLoading(true)
    try {
      setSecurityEvents(await auditApi.securityEvents({
        action: actionFilter || undefined,
        email: emailFilter.trim() || undefined,
        from: fromIso(securityFrom),
        to: toIso(securityTo),
      }))
      setError('')
    } catch (requestError) {
      setError(getApiError(requestError))
    } finally {
      setSecurityLoading(false)
    }
  }, [actionFilter, emailFilter, securityFrom, securityTo])

  useEffect(() => { void loadDraftEvents() }, [loadDraftEvents])

  // Debounce the free-text email filter; selects and dates apply immediately through the same effect.
  useEffect(() => {
    const timer = setTimeout(() => { void loadSecurityEvents() }, 250)
    return () => clearTimeout(timer)
  }, [loadSecurityEvents])

  return (
    <div className="page audit-log-page">
      <h1 className="sr-only">Audit log</h1>

      {error && <ErrorBanner>{error}</ErrorBanner>}

      <section className="panel admin-module-panel" aria-labelledby="draft-events-title">
        <div className="directory-toolbar">
          <div>
            <span className="eyebrow">Append-only</span>
            <h2 id="draft-events-title">Draft events</h2>
          </div>
        </div>
        <p className="field-hint">
          One immutable record per accepted transition and pick. Records are never edited or deleted —
          admin recovery appends a compensating <code>AdminRecoveryApplied</code> event instead.
        </p>
        <div className="audit-filters">
          <label className="field" htmlFor="filter-draft">
            <span className="field-label">Draft</span>
            <select id="filter-draft" value={draftFilter} onChange={(event) => setDraftFilter(event.target.value)}>
              <option value="">All drafts</option>
              {drafts.map((draft) => <option key={draft.id} value={draft.id}>{draft.name} · {draft.code}</option>)}
            </select>
          </label>
          <label className="field" htmlFor="filter-type">
            <span className="field-label">Event type</span>
            <select id="filter-type" value={typeFilter} onChange={(event) => setTypeFilter(event.target.value)}>
              <option value="">All types</option>
              {DRAFT_EVENT_TYPES.map((type) => <option key={type} value={type}>{type}</option>)}
            </select>
          </label>
          <label className="field" htmlFor="filter-actor">
            <span className="field-label">Actor</span>
            <select id="filter-actor" value={actorFilter} onChange={(event) => setActorFilter(event.target.value)}>
              <option value="">Anyone</option>
              {users.map((user) => <option key={user.id} value={user.id}>{user.displayName}</option>)}
            </select>
          </label>
          <label className="field" htmlFor="filter-draft-from">
            <span className="field-label">From</span>
            <input id="filter-draft-from" type="date" value={draftFrom} onChange={(event) => setDraftFrom(event.target.value)} />
          </label>
          <label className="field" htmlFor="filter-draft-to">
            <span className="field-label">To</span>
            <input id="filter-draft-to" type="date" value={draftTo} onChange={(event) => setDraftTo(event.target.value)} />
          </label>
        </div>
        {draftEventsLoading ? <LoadingState>Loading draft events…</LoadingState> : draftEvents.length ? (
          <div className="table-scroll">
            <table className="users-table audit-table">
              <thead>
                <tr><th>Draft</th><th>#</th><th>Event</th><th>Transition</th><th>Actor</th><th>Reason</th><th>When</th></tr>
              </thead>
              <tbody>
                {draftEvents.map((event) => (
                  <tr key={`${event.draftId}-${event.sequence}`}>
                    <td data-label="Draft"><strong>{event.draftName}</strong> <code>{event.draftCode}</code></td>
                    <td data-label="#">{event.sequence}</td>
                    <td data-label="Event">{event.type}</td>
                    <td data-label="Transition">{event.fromStatus ? `${event.fromStatus} → ${event.toStatus ?? '—'}` : event.toStatus ?? '—'}</td>
                    <td data-label="Actor">{event.actorName ?? (event.actorUserId ? `${event.actorUserId.slice(0, 8)}…` : 'System')}</td>
                    <td data-label="Reason">{event.reason ?? '—'}</td>
                    <td data-label="When"><time dateTime={event.createdAt}>{new Date(event.createdAt).toLocaleString()}</time></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : <div className="empty-list"><ScrollText /><strong>No matching draft events</strong><span>Loosen the filters to see more of the trail.</span></div>}
      </section>

      <section className="panel admin-module-panel" aria-labelledby="security-events-title">
        <div className="directory-toolbar">
          <div>
            <span className="eyebrow">Account, configuration &amp; admin actions</span>
            <h2 id="security-events-title">Security &amp; admin events</h2>
          </div>
        </div>
        <div className="audit-filters">
          <label className="field" htmlFor="filter-action">
            <span className="field-label">Action</span>
            <select id="filter-action" value={actionFilter} onChange={(event) => setActionFilter(event.target.value)}>
              <option value="">All actions</option>
              {SECURITY_ACTIONS.map((action) => <option key={action} value={action}>{action}</option>)}
            </select>
          </label>
          <label className="field" htmlFor="filter-email">
            <span className="field-label">Email contains</span>
            <span className="search-control audit-email-filter">
              <Search aria-hidden="true" />
              <input id="filter-email" value={emailFilter} onChange={(event) => setEmailFilter(event.target.value)} placeholder="name@example.com" />
            </span>
          </label>
          <label className="field" htmlFor="filter-security-from">
            <span className="field-label">From</span>
            <input id="filter-security-from" type="date" value={securityFrom} onChange={(event) => setSecurityFrom(event.target.value)} />
          </label>
          <label className="field" htmlFor="filter-security-to">
            <span className="field-label">To</span>
            <input id="filter-security-to" type="date" value={securityTo} onChange={(event) => setSecurityTo(event.target.value)} />
          </label>
        </div>
        {securityLoading ? <LoadingState>Loading security events…</LoadingState> : securityEvents.length ? (
          <div className="table-scroll">
            <table className="users-table audit-table">
              <thead>
                <tr><th>Action</th><th>Who</th><th>Detail</th><th>IP</th><th>When</th></tr>
              </thead>
              <tbody>
                {securityEvents.map((event) => (
                  <tr key={event.id}>
                    <td data-label="Action">{event.action}</td>
                    <td data-label="Who">{event.email ?? '—'}</td>
                    <td data-label="Detail">{event.detail ?? '—'}</td>
                    <td data-label="IP">{event.ipAddress ?? '—'}</td>
                    <td data-label="When"><time dateTime={event.createdAt}>{new Date(event.createdAt).toLocaleString()}</time></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : <div className="empty-list"><ShieldCheck /><strong>No matching events</strong><span>Sign-ins, credential changes, and admin actions appear here.</span></div>}
      </section>
    </div>
  )
}
