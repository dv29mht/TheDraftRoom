import { CheckCircle2, ClipboardList, RefreshCw, Search, Star, Timer } from 'lucide-react'
import { FormEvent, useCallback, useEffect, useState } from 'react'
import { clubsApi, getApiError, rosterTemplatesApi } from '../services/api'
import type { Club, RosterTemplateDetail, RosterTemplateSummary } from '../types/admin'
import { SuccessBanner } from '../components/ui/Feedback'

export function AdminTemplatesPage() {
  const [templates, setTemplates] = useState<RosterTemplateSummary[]>([])
  const [active, setActive] = useState<RosterTemplateDetail | null>(null)
  const [eligible, setEligible] = useState<Club[]>([])
  const [search, setSearch] = useState('')
  const [results, setResults] = useState<Club[]>([])
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const [list, activeDetail, eligibleClubs] = await Promise.all([
        rosterTemplatesApi.list(),
        rosterTemplatesApi.active().catch(() => null),
        clubsApi.eligible().catch(() => [])
      ])
      setTemplates(list)
      setActive(activeDetail)
      setEligible(eligibleClubs)
    } catch (requestError) { setError(getApiError(requestError)) }
    finally { setLoading(false) }
  }, [])

  useEffect(() => { void load() }, [load])

  const activate = async (template: RosterTemplateSummary) => {
    setBusy(template.id)
    setError('')
    setNotice('')
    try {
      await rosterTemplatesApi.activate(template.id)
      setNotice(`“${template.name}” is now the active roster template.`)
      await load()
    } catch (requestError) { setError(getApiError(requestError)) }
    finally { setBusy('') }
  }

  const runSearch = async (event: FormEvent) => {
    event.preventDefault()
    if (!search.trim()) return
    setError('')
    try {
      setResults(await clubsApi.search(search.trim()))
    } catch (requestError) { setError(getApiError(requestError)) }
  }

  const toggleFiveStar = async (club: Club, eligibleNext: boolean) => {
    setBusy(club.id)
    setError('')
    setNotice('')
    try {
      const updated = await clubsApi.setFiveStar(club.id, eligibleNext)
      setResults((current) => current.map((item) => item.id === updated.id ? updated : item))
      await load()
    } catch (requestError) { setError(getApiError(requestError)) }
    finally { setBusy('') }
  }

  if (loading) return <div className="page"><div className="panel loading-state" role="status"><RefreshCw className="spin" /> Loading roster templates…</div></div>

  return (
    <div className="page">
      <h1 className="sr-only">Roster templates</h1>
      {error && <div className="form-error" role="alert">{error}</div>}
      {notice && <SuccessBanner onDismiss={() => setNotice('')}>{notice}</SuccessBanner>}

      {active && (
        <section className="panel">
          <div className="panel-heading"><div><span className="eyebrow">Active template</span><h2>{active.summary.name}</h2></div><span className="timer-chip"><Timer /> {active.summary.pickTimerSeconds}s per pick</span></div>
          <div className="slot-grid">
            {active.slots.map((slot) => (
              <span key={slot.order} className={`slot-chip slot-${slot.slotType.toLowerCase()}`}>
                <small>{slot.order === 0 ? 'Held' : `#${slot.order}`}</small>
                <strong>{slot.position ?? slot.label}</strong>
              </span>
            ))}
          </div>
        </section>
      )}

      <section className="panel">
        <div className="panel-heading"><div><span className="eyebrow">Configuration</span><h2>Formations</h2></div></div>
        <p className="field-hint">Every FIFA formation below is selectable per lobby when a host sets up a draft. Activating one just sets the default that new lobbies start on.</p>
        <div className="table-scroll">
          <table className="users-table">
            <thead><tr><th scope="col" className="col-grow">Template</th><th scope="col">Status</th><th scope="col">Slots</th><th scope="col">Timer</th><th scope="col"><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>{templates.map((template) => (
              <tr key={template.id}>
                <td data-label="Template" className="col-grow"><strong>{template.name}</strong></td>
                <td data-label="Status"><span className={`status-label ${template.isActive ? 'active' : 'pending'}`}><span />{template.isActive ? 'Active' : 'Inactive'}</span></td>
                <td data-label="Slots">{template.slotCount}</td>
                <td data-label="Timer">{template.pickTimerSeconds}s</td>
                <td data-label="Action"><div className="table-actions">
                  {!template.isActive && <button className="secondary-button" onClick={() => void activate(template)} disabled={busy === template.id}>{busy === template.id ? 'Activating…' : 'Activate'}</button>}
                </div></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      </section>

      <section className="panel">
        <div className="panel-heading"><div><span className="eyebrow">Draft clubs</span><h2>Eligible clubs (5★ / 4.5★)</h2></div><span className="eyebrow">{eligible.length} selected</span></div>
        <div className="chip-row">
          {eligible.length ? eligible.map((club) => (
            <button key={club.id} className="club-chip selected" onClick={() => void toggleFiveStar(club, false)} disabled={busy === club.id} aria-label={`Remove ${club.name} from eligible clubs`}>
              <Star /> {club.name}
            </button>
          )) : <p className="role-source-note">No eligible clubs selected yet. Search below to add eligible Kick Off clubs.</p>}
        </div>
        <form className="user-form" onSubmit={runSearch}>
          <label className="search-control"><Search aria-hidden="true" /><span className="sr-only">Search clubs</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search club or league" /></label>
          <button className="secondary-button" type="submit">Search</button>
        </form>
        {results.length > 0 && (
          <ul className="club-result-list">
            {results.map((club) => (
              <li key={club.id}>
                <span><strong>{club.name}</strong><small>{club.league}</small></span>
                <button className={`secondary-button ${club.isFiveStarEligible ? 'is-on' : ''}`} onClick={() => void toggleFiveStar(club, !club.isFiveStarEligible)} disabled={busy === club.id} aria-pressed={club.isFiveStarEligible}>
                  <Star /> {club.isFiveStarEligible ? 'Eligible' : 'Mark eligible'}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="panel data-rules-card">
        <span className="eyebrow"><ClipboardList /> Snapshot rule</span><h2>Draft freezing</h2>
        <ul>
          <li><CheckCircle2 /> A draft snapshots the active template's ordered slots and timer when it starts</li>
          <li><CheckCircle2 /> Editing or reactivating templates never changes an in-progress draft</li>
          <li><CheckCircle2 /> Only eligible 5★/4.5★ clubs from the active dataset are offered in a draft</li>
        </ul>
      </section>
    </div>
  )
}
