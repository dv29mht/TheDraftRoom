import { CalendarClock, DraftingCompass, Plus, Radio, RefreshCw, Users, UsersRound } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { draftsApi, getApiError } from '../services/api'
import type { DraftSummary } from '../types/draft'

export function AdminDraftsPage() {
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

  const oneVOne = drafts.filter((draft) => draft.format === '1v1').length
  const twoVTwo = drafts.filter((draft) => draft.format === '2v2').length

  return (
    <div className="page">
      <section className="stat-grid">
        <article><span className="stat-icon primary"><DraftingCompass /></span><div><strong>{drafts.length}</strong><small>Total drafts</small></div></article>
        <article><span className="stat-icon accent"><UsersRound /></span><div><strong>{oneVOne}</strong><small>1v1 drafts</small></div></article>
        <article><span className="stat-icon gold"><Radio /></span><div><strong>{twoVTwo}</strong><small>2v2 drafts</small></div></article>
      </section>

      {error && <div className="form-error" role="alert">{error}</div>}
      <section className="panel admin-module-panel">
        <div className="directory-toolbar">
          <div><span className="eyebrow">Operations</span><h2>All drafts</h2></div>
          <Link className="primary-button compact" to="/drafts/new"><Plus /> Create lobby</Link>
        </div>
        {loading ? <div className="loading-state"><RefreshCw className="spin" /> Loading drafts…</div> : drafts.length ? (
          <div className="admin-card-list">
            {drafts.map((draft) => <Link className="admin-list-card" key={draft.id} to={`/drafts/${draft.id}`}>
              <span className="admin-list-icon"><DraftingCompass /></span>
              <div><strong>{draft.name}</strong><small><Users aria-hidden="true" /> {draft.participantCount} in lobby</small></div>
              <span className="card-badges"><span className="format-badge">{draft.format}</span><span className={`status-pill status-${draft.status.toLowerCase()}`}>{draft.status}</span></span>
              <code>{draft.code}</code>
              <time dateTime={draft.createdAt}><CalendarClock /> {new Date(draft.createdAt).toLocaleString()}</time>
            </Link>)}
          </div>
        ) : <div className="empty-list"><DraftingCompass /><strong>No drafts yet</strong><span>Create the first lobby to begin tracking operations.</span><Link className="secondary-button" to="/drafts/new">Create a lobby</Link></div>}
      </section>
    </div>
  )
}
