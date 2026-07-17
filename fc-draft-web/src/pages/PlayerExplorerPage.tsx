import { ChevronLeft, ChevronRight, ExternalLink, Search, SlidersHorizontal, Star, UsersRound, X, Zap } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { getApiError, playersApi } from '../services/api'
import type { FcPlayer, PlayerFilterOptions } from '../data/fc26Players'
import { Modal } from '../components/ui/Modal'

const pageSize = 48

function StarRating({ label, value }: { label: string; value: number }) {
  return <span className="player-star-rating" aria-label={`${label}: ${value} out of 5`}><small>{label}</small><b aria-hidden="true">{'★'.repeat(value)}<i>{'★'.repeat(5 - value)}</i></b></span>
}

function PlayerPortrait({ player, className = '' }: { player: FcPlayer; className?: string }) {
  const [failed, setFailed] = useState(false)

  if (failed || !player.imageUrl) return <span className={`player-image-fallback ${className}`} aria-hidden="true"><UsersRound /></span>

  return (
    <img
      className={className}
      src={player.imageUrl}
      alt={`${player.name} portrait`}
      width="360"
      height="360"
      loading="lazy"
      onError={() => setFailed(true)}
    />
  )
}

function PlayerDetails({ player, onClose }: { player: FcPlayer; onClose: () => void }) {
  return (
    <Modal
      onClose={onClose}
      labelledBy="player-detail-title"
      backdropClassName="player-modal-backdrop"
      dialogClassName="player-modal"
    >
        <button className="player-modal-close" type="button" onClick={onClose} aria-label={`Close ${player.name} details`}><X /></button>
        <div className="player-modal-hero">
          <div className="player-modal-rating"><strong>{player.overall}</strong><span>OVR</span><b>{player.position}</b></div>
          <PlayerPortrait player={player} className="player-modal-image" />
          <div className="player-modal-identity">
            <span className="eyebrow">EA SPORTS FC 26</span>
            <h2 id="player-detail-title">{player.name}</h2>
            <p>{player.club} <span aria-hidden="true">·</span> {player.nation}</p>
            <div className="position-list" aria-label="Eligible positions">
              <span className="position-primary">{player.position}<small>Primary</small></span>
              {player.alternatePositions.map((position) => <span key={position}>{position}<small>Alternate</small></span>)}
            </div>
          </div>
        </div>
        <div className="player-modal-content">
          <div>
            <h3>Card attributes</h3>
            <div className="detail-stat-grid">
              {player.stats.map((stat) => <span key={stat.label}><strong>{stat.value}</strong>{stat.label}</span>)}
            </div>
            <dl className="player-bio">
              <div><dt>Strong foot</dt><dd>{player.preferredFoot}</dd></div>
              <div><dt>Weak foot</dt><dd><StarRating label="Weak foot" value={player.weakFoot} /></dd></div>
              <div><dt>Skill moves</dt><dd><StarRating label="Skill moves" value={player.skillMoves} /></dd></div>
              <div><dt>Height</dt><dd>{player.height}</dd></div>
              <div><dt>League</dt><dd>{player.league}</dd></div>
            </dl>
          </div>
          <div>
            <h3>Roles <span>{player.roles.length}</span></h3>
            {player.roles.length ? <div className="role-list">{player.roles.map((role) => (
              <span key={`${role.position}-${role.name}`}><b>{role.position}</b>{role.name}<strong>{'+'.repeat(role.familiarity)}</strong></span>
            ))}</div> : <p className="role-source-note">No Role+ or Role++ familiarity is listed for this player.</p>}
            <h3>PlayStyles <span>{player.playstyles.length}</span></h3>
            <div className="playstyle-list">
              {player.playstyles.map((playstyle) => (
                <span className={playstyle.plus ? 'plus' : ''} key={playstyle.name}><Zap />{playstyle.name}{playstyle.plus && <b>+</b>}</span>
              ))}
            </div>
            {player.sourceUrl && <a className="ea-source-link" href={player.sourceUrl} target="_blank" rel="noreferrer">View official EA rating <ExternalLink /></a>}
          </div>
        </div>
    </Modal>
  )
}

export function PlayerExplorerPage() {
  const [players, setPlayers] = useState<FcPlayer[]>([])
  const [datasetLabel, setDatasetLabel] = useState('')
  const [total, setTotal] = useState(0)
  const [totalPages, setTotalPages] = useState(1)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState('')
  const [query, setQuery] = useState('')
  const [position, setPosition] = useState('All')
  const [minimum, setMinimum] = useState(75)
  const [league, setLeague] = useState('All')
  const [nation, setNation] = useState('All')
  const [sort, setSort] = useState('overall_desc')
  const [options, setOptions] = useState<PlayerFilterOptions>({ positions: [], leagues: [], nations: [] })
  const [shortlist, setShortlist] = useState<number[]>([])
  const [activePlayer, setActivePlayer] = useState<FcPlayer | null>(null)

  useEffect(() => {
    playersApi.filters().then(setOptions).catch(() => { /* filters are best-effort */ })
  }, [])

  const load = useCallback(async (requestedPage: number) => {
    setLoading(true)
    setLoadError('')
    try {
      const result = await playersApi.search({
        search: query.trim() || undefined,
        position: position === 'All' ? undefined : position,
        minOverall: minimum,
        league: league === 'All' ? undefined : league,
        nation: nation === 'All' ? undefined : nation,
        sort,
        page: requestedPage,
        pageSize
      })
      setPlayers(result.items)
      setDatasetLabel(result.datasetLabel)
      setTotal(result.total)
      setTotalPages(result.totalPages)
      setPage(result.page)
    } catch (error) {
      setLoadError(getApiError(error))
    } finally {
      setLoading(false)
    }
  }, [query, position, minimum, league, nation, sort])

  // Debounce so typing in the search box does not fire a request per keystroke.
  useEffect(() => {
    const timer = window.setTimeout(() => { void load(1) }, 250)
    return () => window.clearTimeout(timer)
  }, [load])

  // Focus returns to the triggering card via the shared Modal's focus trap.
  const closeDetails = () => setActivePlayer(null)

  return (
    <div className="page explorer-page">
      <h1 className="sr-only">Player explorer</h1>
      <section className="explorer-toolbar panel">
        <label className="search-control explorer-search"><Search /><span className="sr-only">Search players</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search player or club" />{query && <button type="button" onClick={() => setQuery('')} aria-label="Clear search"><X /></button>}</label>
        <label className="filter-control"><SlidersHorizontal /><span>Position</span><select value={position} onChange={(event) => setPosition(event.target.value)}><option>All</option>{options.positions.map((item) => <option key={item}>{item}</option>)}</select></label>
        <label className="filter-control"><Star /><span>Min OVR</span><select value={minimum} onChange={(event) => setMinimum(Number(event.target.value))}><option value={75}>75</option><option value={80}>80</option><option value={85}>85</option><option value={90}>90</option></select></label>
        <label className="filter-control"><SlidersHorizontal /><span>League</span><select value={league} onChange={(event) => setLeague(event.target.value)}><option>All</option>{options.leagues.map((item) => <option key={item}>{item}</option>)}</select></label>
        <label className="filter-control"><SlidersHorizontal /><span>Nation</span><select value={nation} onChange={(event) => setNation(event.target.value)}><option>All</option>{options.nations.map((item) => <option key={item}>{item}</option>)}</select></label>
        <label className="filter-control"><SlidersHorizontal /><span>Sort</span><select value={sort} onChange={(event) => setSort(event.target.value)}><option value="overall_desc">Rating (high→low)</option><option value="overall_asc">Rating (low→high)</option><option value="name_asc">Name (A→Z)</option><option value="name_desc">Name (Z→A)</option></select></label>
      </section>
      <div className="results-meta"><strong>{total.toLocaleString()} players</strong><span>{datasetLabel || 'Loading dataset'} · {shortlist.length} shortlisted</span></div>
      {loading && <section className="panel loading-state" role="status"><span className="spinner" /> Loading eligible players…</section>}
      {loadError && <section className="form-error" role="alert">{loadError}</section>}
      {!loading && !loadError && (
        <section className="player-grid" aria-label="FC 26 players">
          {players.map((player) => {
            const selected = shortlist.includes(player.id)
            const featuredStyle = player.playstyles.find((playstyle) => playstyle.plus) ?? player.playstyles[0]
            return (
              <article className="player-card" key={player.id}>
                <button
                  className="player-card-open"
                  type="button"
                  aria-haspopup="dialog"
                  aria-label={`View ${player.name}, ${player.overall} rated ${player.position}`}
                  onClick={() => setActivePlayer(player)}
                >
                  <span className="player-card-top"><span className="overall">{player.overall}<small>OVR</small></span></span>
                  <PlayerPortrait player={player} className="player-card-image" />
                  <span className="player-card-body">
                    <span className="position-tag">{player.position}{player.alternatePositions.length ? ` · ${player.alternatePositions.join(' / ')}` : ''}</span>
                    <span className="player-name">{player.name}</span>
                    <span className="player-club">{player.club} · {player.nation}</span>
                    <span className="player-card-foot"><b>{player.preferredFoot}</b> foot <StarRating label="Weak foot" value={player.weakFoot} /><StarRating label="Skill moves" value={player.skillMoves} /></span>
                    {featuredStyle && <span className={`card-playstyle ${featuredStyle.plus ? 'plus' : ''}`}><Zap />{featuredStyle.name}{featuredStyle.plus ? '+' : ''}<small>{player.playstyles.length} PlayStyles</small></span>}
                    <span className="attribute-grid">{player.stats.slice(0, 4).map((stat) => <span key={stat.label}><strong>{stat.value}</strong>{stat.label}</span>)}</span>
                    <span className="view-card-hint">View full card <span aria-hidden="true">→</span></span>
                  </span>
                </button>
                <button className={`shortlist-button ${selected ? 'selected' : ''}`} type="button" onClick={() => setShortlist((current) => selected ? current.filter((id) => id !== player.id) : [...current, player.id])} aria-label={`${selected ? 'Remove' : 'Add'} ${player.name} ${selected ? 'from' : 'to'} shortlist`} aria-pressed={selected}><Star /></button>
              </article>
            )
          })}
        </section>
      )}
      {!loading && !loadError && total > 0 && (
        <footer className="explorer-pagination">
          <span>Page {page} of {totalPages}</span>
          <div className="pagination-actions">
            <button className="icon-button" disabled={page <= 1} onClick={() => void load(page - 1)} aria-label="Previous page"><ChevronLeft /></button>
            <button className="icon-button" disabled={page >= totalPages} onClick={() => void load(page + 1)} aria-label="Next page"><ChevronRight /></button>
          </div>
        </footer>
      )}
      {!loading && !players.length && !loadError && <section className="panel empty-list"><Search /><strong>No eligible players found</strong><span>Adjust the position, rating or search term.</span></section>}
      {activePlayer && <PlayerDetails player={activePlayer} onClose={closeDetails} />}
    </div>
  )
}
