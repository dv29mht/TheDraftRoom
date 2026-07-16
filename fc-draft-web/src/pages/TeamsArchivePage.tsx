import { CalendarClock, RefreshCw, Shield, Trophy } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { draftsApi, getApiError } from '../services/api'
import { useAuthStore } from '../stores/authStore'
import type { DraftResults, DraftSummary } from '../types/draft'

const ARCHIVE_LIMIT = 12

// The squad archive (PR-19, §9.7): every completed draft the viewer can see, with THEIR squad's headline
// numbers, linking into the full read-only results. Results are immutable server-side, so this page is a
// pure read — the newest ARCHIVE_LIMIT drafts load their result cards eagerly.
export function TeamsArchivePage() {
  const userId = useAuthStore((state) => state.user?.id)
  const [completed, setCompleted] = useState<DraftSummary[]>([])
  const [resultsById, setResultsById] = useState<Record<string, DraftResults>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    const load = async () => {
      try {
        const drafts = await draftsApi.list()
        const finished = drafts
          .filter((draft) => draft.status === 'Completed')
          .sort((a, b) => (b.completedAt ?? b.createdAt).localeCompare(a.completedAt ?? a.createdAt))
        if (!active) return
        setCompleted(finished)

        const cards = await Promise.all(finished.slice(0, ARCHIVE_LIMIT).map(async (draft) => {
          try { return [draft.id, await draftsApi.results(draft.id)] as const }
          catch { return null } // e.g. recovered/edge drafts — the card simply shows without ratings
        }))
        if (!active) return
        setResultsById(Object.fromEntries(cards.filter((entry): entry is [string, DraftResults] => entry != null)))
      } catch (requestError) {
        if (active) setError(getApiError(requestError))
      } finally {
        if (active) setLoading(false)
      }
    }
    void load()
    return () => { active = false }
  }, [])

  return (
    <div className="page">
      <section className="page-heading">
        <div><span className="eyebrow">Squad archive</span><h1>Completed teams</h1></div>
      </section>

      {error && <div className="form-error" role="alert">{error}</div>}

      {loading ? (
        <div className="loading-state"><RefreshCw className="spin" /> Loading your archive…</div>
      ) : completed.length === 0 ? (
        <section className="panel placeholder-panel">
          <span className="empty-orb"><Trophy /></span>
          <span className="eyebrow">Squad archive</span>
          <h2>No completed drafts yet</h2>
          <p>Finish a draft and every squad lands here — formations, ratings, and the full pick history, preserved forever.</p>
          <Link to="/drafts">Go to the draft hub</Link>
        </section>
      ) : (
        <section className="panel admin-module-panel">
          <div className="directory-toolbar">
            <div><span className="eyebrow">Archive</span><h2>{completed.length} completed draft{completed.length === 1 ? '' : 's'}</h2></div>
          </div>
          <div className="admin-card-list">
            {completed.map((draft) => {
              const results = resultsById[draft.id]
              const myTeam = results?.teams.find((team) => userId != null && team.memberUserIds.includes(userId))
              const headline = myTeam ?? results?.teams[0]
              return (
                <Link className="admin-list-card" key={draft.id} to={`/drafts/${draft.id}/results`}>
                  <span className="admin-list-icon"><Trophy /></span>
                  <div>
                    <strong>{draft.name}</strong>
                    <small>
                      {headline
                        ? <>{myTeam ? 'Your squad' : headline.name}{headline.averageOverall != null ? ` · ${headline.averageOverall} avg` : ''}{headline.selectedClubName ? <> · <Shield aria-hidden="true" /> {headline.selectedClubName}</> : ''}</>
                        : 'View results'}
                    </small>
                  </div>
                  <span className="card-badges"><span className="format-badge">{draft.format}</span><span className="status-pill status-completed">Completed</span></span>
                  <code>{draft.code}</code>
                  <time dateTime={draft.completedAt ?? draft.createdAt}>
                    <CalendarClock /> {new Date(draft.completedAt ?? draft.createdAt).toLocaleDateString()}
                  </time>
                </Link>
              )
            })}
          </div>
        </section>
      )}
    </div>
  )
}
