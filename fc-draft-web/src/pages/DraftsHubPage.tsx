import { CalendarClock, DraftingCompass, Plus, RefreshCw, Users } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { draftsApi, getApiError } from '../services/api'
import type { DraftSummary } from '../types/draft'

// PR-19 (§9.7): the hub groups drafts by where they are in their life — live play first, then lobbies
// still gathering, then the archive. Completed drafts open their results; everything else opens the room.
const LIVE_STATUSES = new Set(['SpinnerRanking', 'ClubSelection', 'PositionDraft', 'Paused'])
const UPCOMING_STATUSES = new Set(['Lobby', 'TeamFormation', 'ReadyCheck'])

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

  const live = drafts.filter((draft) => LIVE_STATUSES.has(draft.status))
  const upcoming = drafts.filter((draft) => UPCOMING_STATUSES.has(draft.status))
  const completed = drafts.filter((draft) => draft.status === 'Completed')
  const ended = drafts.filter((draft) => draft.status === 'Cancelled' || draft.status === 'Abandoned')

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
          <DraftList title="Live now" drafts={live} />
          <DraftList title="Upcoming" drafts={upcoming} />
          <DraftList title="Completed" drafts={completed} linkTo={(draft) => `/drafts/${draft.id}/results`} />
          <DraftList title="Ended early" drafts={ended} />
        </>
      )}
    </div>
  )
}

function DraftList({ title, drafts, linkTo }: {
  title: string
  drafts: DraftSummary[]
  linkTo?: (draft: DraftSummary) => string
}) {
  if (drafts.length === 0) return null
  return (
    <section className="panel admin-module-panel">
      <div className="directory-toolbar"><div><span className="eyebrow">{title}</span><h2>{drafts.length} draft{drafts.length === 1 ? '' : 's'}</h2></div></div>
      <div className="admin-card-list">
        {drafts.map((draft) => (
          <Link className="admin-list-card" key={draft.id} to={linkTo ? linkTo(draft) : `/drafts/${draft.id}`}>
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
