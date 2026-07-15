import { CalendarClock, DraftingCompass, Plus, RefreshCw, Users } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { draftsApi, getApiError } from '../services/api'
import type { DraftSummary } from '../types/draft'

const LIVE_STATUSES = new Set(['Lobby', 'TeamFormation', 'ReadyCheck', 'SpinnerRanking', 'ClubSelection', 'PositionDraft', 'Paused'])

export function DraftsHubPage() {
  const [drafts, setDrafts] = useState<DraftSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    draftsApi.list()
      .then((next) => { if (active) setDrafts(next) })
      .catch((requestError) => { if (active) setError(getApiError(requestError)) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])

  const active = drafts.filter((draft) => LIVE_STATUSES.has(draft.status))
  const finished = drafts.filter((draft) => !LIVE_STATUSES.has(draft.status))

  return (
    <div className="page">
      <section className="page-heading">
        <div><span className="eyebrow">Draft hub</span><h1>Your tournament drafts</h1></div>
        <Link className="primary-button compact" to="/drafts/new"><Plus /> New lobby</Link>
      </section>

      {error && <div className="form-error" role="alert">{error}</div>}

      {loading ? (
        <div className="loading-state"><RefreshCw className="spin" /> Loading your drafts…</div>
      ) : drafts.length === 0 ? (
        <section className="panel placeholder-panel">
          <span className="empty-orb"><DraftingCompass /></span>
          <span className="eyebrow">Draft hub</span>
          <h2>No drafts yet</h2>
          <p>Create a 1v1 or 2v2 lobby, invite your players, and confirm attendance to get started.</p>
          <Link className="primary-button compact" to="/drafts/new"><Plus /> Create your first lobby</Link>
        </section>
      ) : (
        <>
          <DraftList title="Active & upcoming" drafts={active} />
          <DraftList title="Finished" drafts={finished} />
        </>
      )}
    </div>
  )
}

function DraftList({ title, drafts }: { title: string; drafts: DraftSummary[] }) {
  if (drafts.length === 0) return null
  return (
    <section className="panel admin-module-panel">
      <div className="directory-toolbar"><div><span className="eyebrow">{title}</span><h2>{drafts.length} draft{drafts.length === 1 ? '' : 's'}</h2></div></div>
      <div className="admin-card-list">
        {drafts.map((draft) => (
          <Link className="admin-list-card" key={draft.id} to={`/drafts/${draft.id}`}>
            <span className="admin-list-icon"><DraftingCompass /></span>
            <div><strong>{draft.name}</strong><small><Users aria-hidden="true" /> {draft.participantCount} in lobby</small></div>
            <span className="card-badges"><span className="format-badge">{draft.format}</span><span className={`status-pill status-${draft.status.toLowerCase()}`}>{draft.status}</span></span>
            <code>{draft.code}</code>
            <time dateTime={draft.createdAt}><CalendarClock /> {new Date(draft.createdAt).toLocaleDateString()}</time>
          </Link>
        ))}
      </div>
    </section>
  )
}
