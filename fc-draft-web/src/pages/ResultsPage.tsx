import { ArrowLeft, DoorOpen, LayoutList, RefreshCw, Shield, Trophy, Volleyball } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { draftsApi, getApiError } from '../services/api'
import { useAuthStore } from '../stores/authStore'
import type { DraftResults, DraftRosterSlot, ResultPick, TeamResult } from '../types/draft'

// A completed draft's immutable results (PR-19, §9.7): every squad in formation and list views, average
// and line ratings, represented clubs/leagues/nations, and the full pick sequence. Strictly read-only —
// the page issues no mutations, so participants and admins can reopen it forever.
export function ResultsPage() {
  const { draftId = '' } = useParams()
  const userId = useAuthStore((state) => state.user?.id)
  const [results, setResults] = useState<DraftResults | null>(null)
  const [loading, setLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [error, setError] = useState('')
  const [focusTeamId, setFocusTeamId] = useState<string | null>(null)
  const [view, setView] = useState<'formation' | 'list'>('formation')

  useEffect(() => {
    let active = true
    draftsApi.results(draftId)
      .then((next) => { if (active) setResults(next) })
      .catch((requestError) => {
        if (!active) return
        const status = (requestError as { response?: { status?: number } })?.response?.status
        if (status === 404) setNotFound(true)
        else setError(getApiError(requestError))
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [draftId])

  if (loading) return <div className="page"><div className="loading-state"><RefreshCw className="spin" /> Loading results…</div></div>
  if (notFound) return (
    <div className="page">
      <section className="panel placeholder-panel">
        <span className="empty-orb"><Trophy /></span>
        <span className="eyebrow">Results</span>
        <h2>No results here</h2>
        <p>This draft has not completed, does not exist, or you are not one of its participants.</p>
        <Link to="/teams">Back to squad archive</Link>
      </section>
    </div>
  )
  if (!results) return <div className="page">{error && <div className="form-error" role="alert">{error}</div>}</div>

  const teams = results.teams
  const focused = teams.find((team) => team.teamId === focusTeamId)
    ?? teams.find((team) => userId != null && team.memberUserIds.includes(userId))
    ?? teams[0]
  const completedAt = results.summary.completedAt ? new Date(results.summary.completedAt).toLocaleDateString() : null

  return (
    <div className="page results-page">
      <Link className="back-link" to="/teams"><ArrowLeft /> Squad archive</Link>

      <section className="panel lobby-header">
        <div className="lobby-title">
          <span className="eyebrow">{results.summary.format} draft · Completed{completedAt ? ` ${completedAt}` : ''}</span>
          <h1>{results.summary.name}</h1>
          <div className="lobby-meta">
            <span className="status-pill status-completed">Completed</span>
            <span className="lobby-code">Code <b>{results.summary.code}</b></span>
          </div>
        </div>
        <Link className="secondary-button" to={`/drafts/${results.summary.id}`}><DoorOpen /> Reopen draft room</Link>
      </section>

      <section className="panel formation-panel">
        <div className="panel-heading">
          <div><span className="eyebrow">Final squads</span><h3>{focused?.name ?? 'Squads'}</h3></div>
          <Trophy aria-hidden="true" />
        </div>

        <div className="team-rail" role="group" aria-label="Teams">
          {teams.map((team) => (
            <button
              key={team.teamId}
              type="button"
              className={`team-rail-chip${focused?.teamId === team.teamId ? ' is-focused' : ''}`}
              aria-pressed={focused?.teamId === team.teamId}
              onClick={() => setFocusTeamId(team.teamId)}
            >
              {team.spinnerRank != null && <span className="spinner-rank">{team.spinnerRank}</span>}
              <span className="team-rail-name">
                <strong>{team.name}{userId != null && team.memberUserIds.includes(userId) ? ' · You' : ''}</strong>
                <small>{team.averageOverall != null ? `${team.averageOverall} avg` : '—'}{team.selectedClubName ? ` · ${team.selectedClubName}` : ''}</small>
              </span>
            </button>
          ))}
        </div>

        {focused && (
          <>
            <div className="results-team-meta">
              {focused.selectedClubName && <span className="status-pill status-joined"><Shield aria-hidden="true" /> {focused.selectedClubName}</span>}
              <span className="results-members">{focused.memberNames.join(' · ')}</span>
            </div>

            <div className="results-ratings" aria-label="Squad ratings">
              <span className="rating-chip rating-total"><strong>{focused.averageOverall ?? '—'}</strong>Squad avg</span>
              {focused.lineRatings.map((line) => (
                <span key={line.line} className="rating-chip"><strong>{line.average ?? '—'}</strong>{line.line}</span>
              ))}
            </div>

            <div className="rail-view-tabs results-view-tabs" role="group" aria-label="Squad view">
              <button type="button" className={`rail-view-tab${view === 'formation' ? ' is-active' : ''}`} aria-pressed={view === 'formation'} onClick={() => setView('formation')}>
                <Volleyball aria-hidden="true" /> Formation
              </button>
              <button type="button" className={`rail-view-tab${view === 'list' ? ' is-active' : ''}`} aria-pressed={view === 'list'} onClick={() => setView('list')}>
                <LayoutList aria-hidden="true" /> List
              </button>
            </div>

            {view === 'formation'
              ? <FormationView team={focused} slots={results.slots} />
              : <SquadListView team={focused} slots={results.slots} />}

            <div className="results-represented">
              <RepresentedRow label="Clubs" values={focused.clubs} />
              <RepresentedRow label="Leagues" values={focused.leagues} />
              <RepresentedRow label="Nations" values={focused.nations} />
            </div>
          </>
        )}
      </section>

      <section className="panel formation-panel">
        <div className="panel-heading"><div><span className="eyebrow">Pick sequence</span><h3>How the draft unfolded</h3></div><LayoutList aria-hidden="true" /></div>
        <ol className="pick-history pick-history-full pick-sequence" aria-label="Pick sequence">
          {results.pickSequence.map((pick) => (
            <li key={pick.sequence} className="pick-history-row">
              <span className="pick-history-number">#{pick.sequence}</span>
              <div>
                <strong>{pick.footballerName} · {pick.footballerOverall}</strong>
                <small>{teams.find((team) => team.teamId === pick.teamId)?.name ?? 'Team'} — {pick.slotLabel}{pick.clubName ? ` · ${pick.clubName}` : ''}</small>
              </div>
            </li>
          ))}
        </ol>
      </section>

      {error && <div className="form-error" role="alert">{error}</div>}
    </div>
  )
}

const LINE_Y: Record<string, number> = { FWD: 16, MID: 42, DEF: 68, GK: 88 }

function lineOf(position: string | null): string | null {
  switch (position) {
    case 'GK': return 'GK'
    case 'LB': case 'CB': case 'RB': case 'LWB': case 'RWB': return 'DEF'
    case 'CM': case 'CDM': case 'CAM': case 'LM': case 'RM': return 'MID'
    case 'ST': case 'CF': case 'LW': case 'RW': return 'FWD'
    default: return null
  }
}

/** Left-to-right placement inside a line: L* first, centre roles, then R*. */
function sideOrder(position: string | null): number {
  if (!position) return 1
  if (position.startsWith('L')) return 0
  if (position.startsWith('R')) return 2
  return 1
}

// The formation view: the starting XI laid out on a pitch from the FROZEN slot positions (any template
// works — slots group into lines and spread evenly), with the held player and flexible bench below.
function FormationView({ team, slots }: { team: TeamResult; slots: DraftRosterSlot[] }) {
  const pickAt = (order: number): ResultPick | undefined => team.picks.find((pick) => pick.slotOrder === order)
  const xi = slots.filter((slot) => slot.order >= 1 && slot.position != null)
  const bench = slots.filter((slot) => slot.order === 0 || slot.position == null)

  const nodes = Object.entries(LINE_Y).flatMap(([line, y]) => {
    const lineSlots = xi
      .filter((slot) => lineOf(slot.position) === line)
      .sort((a, b) => sideOrder(a.position) - sideOrder(b.position) || a.order - b.order)
    return lineSlots.map((slot, index) => {
      const pick = pickAt(slot.order)
      return { slot, pick, x: ((index + 1) / (lineSlots.length + 1)) * 100, y }
    })
  })

  return (
    <div className="formation-view">
      <div className="formation-pitch" role="img" aria-label={`${team.name} starting eleven in formation`}>
        {nodes.map(({ slot, pick, x, y }) => (
          <div key={slot.order} className={`pitch-node${pick ? '' : ' is-empty'}`} style={{ left: `${x}%`, top: `${y}%` }}>
            <strong>{pick ? pick.footballerOverall : '—'}</strong>
            <small>{slot.label}</small>
            <span>{pick ? pick.footballerName : 'Unfilled'}</span>
          </div>
        ))}
      </div>
      <ul className="squad-slots bench-row" aria-label="Held player and bench">
        {bench.map((slot) => {
          const pick = pickAt(slot.order)
          return (
            <li key={slot.order} className={`squad-slot${pick ? ' is-filled' : ''}`}>
              <span className="squad-slot-label">{slot.order === 0 ? 'Held' : slot.label}</span>
              {pick
                ? <span className="squad-slot-pick">{pick.footballerName} · {pick.footballerOverall}</span>
                : <span className="squad-slot-empty">—</span>}
            </li>
          )
        })}
      </ul>
    </div>
  )
}

// The list view: every slot in order with the frozen pick facts plus the pinned-dataset extras.
function SquadListView({ team, slots }: { team: TeamResult; slots: DraftRosterSlot[] }) {
  const pickAt = (order: number): ResultPick | undefined => team.picks.find((pick) => pick.slotOrder === order)
  return (
    <ul className="pick-history results-squad-list" aria-label={`${team.name} squad list`}>
      {[...slots].sort((a, b) => a.order - b.order).map((slot) => {
        const pick = pickAt(slot.order)
        return (
          <li key={slot.order} className="pick-history-row">
            <span className="pick-history-number">{slot.order === 0 ? 'Held' : slot.label}</span>
            <div>
              <strong>{pick ? `${pick.footballerName} · ${pick.footballerOverall}` : '—'}</strong>
              {pick && <small>{[pick.clubName, pick.league, pick.nation].filter(Boolean).join(' · ')}</small>}
            </div>
          </li>
        )
      })}
    </ul>
  )
}

function RepresentedRow({ label, values }: { label: string; values: string[] }) {
  if (values.length === 0) return null
  return (
    <div className="represented-row">
      <span className="represented-label">{label}</span>
      <div className="chip-row">
        {values.map((value) => <span key={value} className="club-chip represented-chip">{value}</span>)}
      </div>
    </div>
  )
}
