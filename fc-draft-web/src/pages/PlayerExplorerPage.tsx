import { ExternalLink, Search, SlidersHorizontal, Star, UsersRound, X, Zap } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { loadFc26Players, positionsFor, type FcPlayer } from '../data/fc26Players'

const pageSize = 48

function StarRating({ label, value }: { label: string; value: number }) {
  return <span className="player-star-rating" aria-label={`${label}: ${value} out of 5`}><small>{label}</small><b aria-hidden="true">{'★'.repeat(value)}<i>{'★'.repeat(5 - value)}</i></b></span>
}

function PlayerPortrait({ player, className = '' }: { player: FcPlayer; className?: string }) {
  const [failed, setFailed] = useState(false)

  if (failed) return <span className={`player-image-fallback ${className}`} aria-hidden="true"><UsersRound /></span>

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
  const closeButton = useRef<HTMLButtonElement>(null)
  const modal = useRef<HTMLElement>(null)

  useEffect(() => {
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    closeButton.current?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
      if (event.key === 'Tab') {
        const focusable = Array.from(modal.current?.querySelectorAll<HTMLElement>('button, a[href]') ?? [])
        const first = focusable[0]
        const last = focusable.at(-1)
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault()
          last?.focus()
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault()
          first?.focus()
        }
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => {
      document.body.style.overflow = previousOverflow
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [onClose])

  return (
    <div className="player-modal-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <section ref={modal} className="player-modal" role="dialog" aria-modal="true" aria-labelledby="player-detail-title">
        <button ref={closeButton} className="player-modal-close" type="button" onClick={onClose} aria-label={`Close ${player.name} details`}><X /></button>
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
            ))}</div> : <p className="role-source-note">Role and Role++ data is not included in EA's public ratings feed.</p>}
            <h3>PlayStyles <span>{player.playstyles.length}</span></h3>
            <div className="playstyle-list">
              {player.playstyles.map((playstyle) => (
                <span className={playstyle.plus ? 'plus' : ''} key={playstyle.name}><Zap />{playstyle.name}{playstyle.plus && <b>+</b>}</span>
              ))}
            </div>
            <a className="ea-source-link" href={player.sourceUrl} target="_blank" rel="noreferrer">View official EA rating <ExternalLink /></a>
          </div>
        </div>
      </section>
    </div>
  )
}

export function PlayerExplorerPage() {
  const [players, setPlayers] = useState<FcPlayer[]>([])
  const [datasetVersion, setDatasetVersion] = useState('')
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState('')
  const [query, setQuery] = useState('')
  const [position, setPosition] = useState('All')
  const [minimum, setMinimum] = useState(75)
  const [shortlist, setShortlist] = useState<number[]>([])
  const [activePlayer, setActivePlayer] = useState<FcPlayer | null>(null)
  const [visibleCount, setVisibleCount] = useState(pageSize)
  const lastTrigger = useRef<HTMLElement | null>(null)

  useEffect(() => {
    let active = true
    loadFc26Players()
      .then((dataset) => { if (active) { setPlayers(dataset.players); setDatasetVersion(dataset.version) } })
      .catch((error: unknown) => { if (active) setLoadError(error instanceof Error ? error.message : 'The player dataset could not be loaded.') })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])

  const positions = useMemo(() => positionsFor(players), [players])
  const filtered = useMemo(() => players.filter((player) => {
    const search = `${player.name} ${player.club} ${player.league} ${player.nation} ${player.preferredFoot} ${player.playstyles.map(({ name }) => name).join(' ')} ${player.roles.map(({ name }) => name).join(' ')}`.toLowerCase()
    const matchesPosition = position === 'All' || player.position === position || player.alternatePositions.includes(position)
    return search.includes(query.trim().toLowerCase()) && matchesPosition && player.overall >= minimum
  }), [minimum, players, position, query])
  const visiblePlayers = filtered.slice(0, visibleCount)

  useEffect(() => { setVisibleCount(pageSize) }, [minimum, position, query])

  const closeDetails = () => {
    setActivePlayer(null)
    window.setTimeout(() => lastTrigger.current?.focus(), 0)
  }

  return (
    <div className="page explorer-page">
      <section className="explorer-toolbar panel">
        <label className="search-control explorer-search"><Search /><span className="sr-only">Search players</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search player, club, nation or PlayStyle" />{query && <button type="button" onClick={() => setQuery('')} aria-label="Clear search"><X /></button>}</label>
        <label className="filter-control"><SlidersHorizontal /><span>Position</span><select value={position} onChange={(event) => setPosition(event.target.value)}>{positions.map((item) => <option key={item}>{item}</option>)}</select></label>
        <label className="filter-control"><Star /><span>Min OVR</span><select value={minimum} onChange={(event) => setMinimum(Number(event.target.value))}><option>75</option><option>80</option><option>85</option><option>90</option></select></label>
      </section>
      <div className="results-meta"><strong>{filtered.length} players</strong><span>{datasetVersion || 'Loading dataset'} · {players.length} total · {shortlist.length} shortlisted</span></div>
      {loading && <section className="panel loading-state" role="status"><span className="spinner" /> Loading all eligible players…</section>}
      {loadError && <section className="form-error" role="alert">{loadError}</section>}
      <section className="player-grid" aria-label="FC 26 players">
        {visiblePlayers.map((player) => {
          const selected = shortlist.includes(player.id)
          const featuredStyle = player.playstyles.find((playstyle) => playstyle.plus) ?? player.playstyles[0]
          return (
            <article className="player-card" key={player.id}>
              <button
                className="player-card-open"
                type="button"
                aria-haspopup="dialog"
                aria-label={`View ${player.name}, ${player.overall} rated ${player.position}`}
                onClick={(event) => { lastTrigger.current = event.currentTarget; setActivePlayer(player) }}
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
      {visibleCount < filtered.length && <div className="load-more"><button className="secondary-button" type="button" onClick={() => setVisibleCount((count) => count + pageSize)}>Load {Math.min(pageSize, filtered.length - visibleCount)} more <span>{visibleCount} of {filtered.length}</span></button></div>}
      {!loading && !filtered.length && !loadError && <section className="panel empty-list"><Search /><strong>No eligible players found</strong><span>Adjust the position, rating or search term.</span></section>}
      {activePlayer && <PlayerDetails player={activePlayer} onClose={closeDetails} />}
    </div>
  )
}
