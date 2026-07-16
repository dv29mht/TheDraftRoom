import { Check, ChevronDown, Info, Search, Star, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { draftsApi } from '../../services/api'
import type { DraftHubStatus } from '../../services/draftHub'
import type { CatalogFootballer, DraftBoard, DraftDetail } from '../../types/draft'
import type { StageMutate } from './common'
import { useDraftRoomChrome } from './common'
import { PickConfirmSheet } from './PickConfirmSheet'
import type { PendingPick } from './PickConfirmSheet'
import { PlayerDetailSheet } from './PlayerDetailSheet'
import { TeamRail } from './TeamRail'
import { TurnCountdown } from './TurnCountdown'
import { useShortlist } from './useShortlist'
import { useAnnouncer } from '../../hooks/useAnnouncer'

const POOL_PAGE = 100
const POOL_MAX = 500

/**
 * The live draft room (PR-18, §9.6): header with the active team/slot/clock/progress/connection, the
 * eligible pool with search and deliberate paging (always the server's pinned-pool board — the client
 * never derives eligibility or turn order), player detail cards, the pick confirmation sheet, the
 * personal shortlist, and the compact team rail. On iPhone the room swaps the global bottom navigation
 * for its own sticky action area (§8.3).
 */
export function DraftRoomStage({ detail, busy, userId, hubStatus, mutate }: {
  detail: DraftDetail
  busy: boolean
  userId: string | undefined
  hubStatus: DraftHubStatus
  mutate: StageMutate
}) {
  const summary = detail.summary
  const turn = detail.turn
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [take, setTake] = useState(POOL_PAGE)
  const [board, setBoard] = useState<DraftBoard | null>(null)
  const [shortlistOnly, setShortlistOnly] = useState(false)
  const [pendingPick, setPendingPick] = useState<PendingPick | null>(null)
  const [detailCardId, setDetailCardId] = useState<number | null>(null)
  const shortlist = useShortlist(summary.id, userId)
  const requestSeq = useRef(0)
  const { announce, announcer } = useAnnouncer()

  useDraftRoomChrome()

  // Announce server-pushed changes (§13): every accepted pick — including
  // auto-picks, which otherwise change the board silently — and turn handoffs.
  const announcedPicks = useRef(detail.picks.length)
  useEffect(() => {
    if (detail.picks.length > announcedPicks.current) {
      const teamName = (id: string) => detail.teams.find((team) => team.id === id)?.name ?? 'a team'
      const fresh = detail.picks.slice(announcedPicks.current)
      announce(fresh
        .map((pick) => `${pick.footballerName} drafted to ${teamName(pick.teamId)}${pick.footballerPosition ? ` as ${pick.footballerPosition}` : ''}.`)
        .join(' '))
    }
    announcedPicks.current = detail.picks.length
  }, [detail.picks, detail.teams, announce])

  const announcedTurn = useRef<string | null>(null)
  useEffect(() => {
    const key = detail.turn.activeTeamId
    if (key == null || announcedTurn.current === key) return
    if (announcedTurn.current != null && detail.turn.activeTeamName) {
      announce(`${detail.turn.activeTeamName} is on the clock${detail.turn.activeSlotLabel ? ` — ${detail.turn.activeSlotLabel}` : ''}.`)
    }
    announcedTurn.current = key
  }, [detail.turn.activeTeamId, detail.turn.activeTeamName, detail.turn.activeSlotLabel, announce])

  // Debounced search: the input narrows the SERVER pool (pinned dataset), never a client-side list.
  useEffect(() => {
    const handle = window.setTimeout(() => setSearch(searchInput.trim()), 250)
    return () => window.clearTimeout(handle)
  }, [searchInput])

  // The board refreshes on every accepted mutation (version) and filter change. A sequence guard keeps a
  // slow stale response from overwriting a fresher pool.
  useEffect(() => {
    const seq = ++requestSeq.current
    void draftsApi
      .board(summary.id, {
        ...(search ? { search } : {}),
        ...(take !== POOL_PAGE ? { take } : {}),
      })
      .then((next) => { if (requestSeq.current === seq) setBoard(next) })
      .catch(() => { /* REST fallback: the next pushed snapshot re-triggers this fetch */ })
  }, [summary.id, summary.version, search, take])

  const isMyTurn = board?.isMyTurn ?? (userId != null && turn.activeTeamMemberUserIds.includes(userId))
  const pool = board?.eligibleFootballers ?? []
  const poolIds = new Set(pool.map((footballer) => footballer.id))
  const takenIds = new Set(detail.picks.map((pick) => pick.footballerId))
  const activeTeamName = turn.activeTeamName ?? 'Draft'
  const slotLabel = turn.activeSlotLabel ?? 'Pick'
  const totalPicks = detail.teams.length * detail.slots.length
  const pickNumber = Math.min(detail.picks.length + 1, Math.max(totalPicks, 1))

  const toPending = (footballer: CatalogFootballer): PendingPick => ({
    footballerId: footballer.id,
    name: footballer.name,
    overall: footballer.overall,
    positions: footballer.positions,
    clubName: footballer.clubName,
  })

  // mutate() resolves either way (failures surface through the lobby error banner + a reloaded snapshot),
  // so the sheet always closes and the refreshed board explains what happened.
  const submitPick = (pick: PendingPick) =>
    void mutate((version) => draftsApi.submitPick(summary.id, pick.footballerId, version))
      .then(() => setPendingPick(null))

  const hubLabel = hubStatus === 'connected' ? 'Live'
    : hubStatus === 'reconnecting' ? 'Reconnecting…'
    : hubStatus === 'connecting' ? 'Connecting…'
    : 'Offline'

  const shortlistRows = shortlist.entries.map((entry) => ({
    entry,
    taken: takenIds.has(entry.id),
    eligibleNow: poolIds.has(entry.id), // membership in the server pool IS eligibility for this slot
  }))

  return (
    <section className="panel formation-panel draft-room" aria-label="Draft room">
      {announcer}
      <header className="draft-room-header">
        <div className="draft-room-turn">
          <span className="eyebrow">
            Round {turn.round ?? '—'}
            {turn.direction === 'Ascending' ? ' · forward order' : turn.direction === 'Descending' ? ' · reverse order' : ''}
            {' · '}Pick {pickNumber} of {totalPicks}
          </span>
          <h2>{activeTeamName} · {slotLabel}</h2>
          <span className="draft-room-need">
            {turn.activeSlotPosition
              ? `Needs a ${turn.activeSlotPosition}`
              : turn.slotAcceptsAnyPosition ? 'Any position — flexible bench slot' : ''}
          </span>
        </div>
        <div className="draft-room-signals">
          {isMyTurn
            ? <span className="your-turn-chip" role="status">Your turn</span>
            : <span className="status-pill status-positiondraft">Waiting</span>}
          <TurnCountdown timer={detail.timer} />
          <span className={`hub-indicator hub-${hubStatus}`} role="status" aria-label={`Live connection: ${hubLabel}`}>
            <i aria-hidden="true" /> {hubLabel}
          </span>
        </div>
      </header>

      <div className="draft-room-body">
        <div className="draft-pool" id="draft-pool">
          <div className="pool-toolbar">
            <div className="invite-search pool-search">
              <Search aria-hidden="true" />
              <input
                type="search"
                value={searchInput}
                placeholder="Search eligible players"
                aria-label="Search eligible players"
                onChange={(event) => setSearchInput(event.target.value)}
              />
              {searchInput && (
                <button className="pool-search-clear" type="button" aria-label="Clear search" onClick={() => setSearchInput('')}>
                  <X />
                </button>
              )}
            </div>
            <button
              type="button"
              className={`secondary-button shortlist-filter${shortlistOnly ? ' is-on' : ''}`}
              aria-pressed={shortlistOnly}
              onClick={() => setShortlistOnly((current) => !current)}
            >
              <Star /> Shortlist ({shortlist.entries.length})
            </button>
          </div>

          {!isMyTurn && (
            <p className="coming-soon-note" role="status">
              Waiting for {activeTeamName} to pick — plan ahead with search and your shortlist.
            </p>
          )}

          {!shortlistOnly && (
            <>
              <ul className="pick-pool draft-room-pool" aria-label="Eligible players">
                {pool.length === 0 && (
                  <li className="invite-empty">
                    {search ? `No eligible players match “${search}” for this slot.` : 'No eligible players are available for this slot.'}
                  </li>
                )}
                {pool.map((footballer) => (
                  <li key={footballer.id} className="pool-row">
                    <div className="pool-row-facts">
                      <strong>{footballer.name}</strong>
                      <small>{footballer.overall} · {footballer.positions.join('/')} · {footballer.clubName}</small>
                    </div>
                    <div className="pool-row-actions">
                      <button
                        type="button"
                        className={`pool-star${shortlist.has(footballer.id) ? ' is-shortlisted' : ''}`}
                        aria-pressed={shortlist.has(footballer.id)}
                        aria-label={shortlist.has(footballer.id) ? `Remove ${footballer.name} from shortlist` : `Add ${footballer.name} to shortlist`}
                        onClick={() => shortlist.toggle({
                          id: footballer.id, name: footballer.name, overall: footballer.overall,
                          positions: footballer.positions, clubName: footballer.clubName,
                        })}
                      >
                        <Star />
                      </button>
                      <button type="button" className="ghost-button" onClick={() => setDetailCardId(footballer.id)} aria-label={`View ${footballer.name} details`}>
                        <Info /> Details
                      </button>
                      {isMyTurn && (
                        <button type="button" className="ghost-button pool-draft" disabled={busy} onClick={() => setPendingPick(toPending(footballer))} aria-label={`Draft ${footballer.name}`}>
                          <Check /> Draft
                        </button>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
              {pool.length >= take && take < POOL_MAX && (
                <div className="load-more">
                  <button className="secondary-button" type="button" onClick={() => setTake((current) => Math.min(POOL_MAX, current + 200))}>
                    <ChevronDown /> Show more players <span>(showing the top {pool.length})</span>
                  </button>
                </div>
              )}
            </>
          )}

          {shortlistOnly && (
            <ul className="pick-pool draft-room-pool" aria-label="Shortlisted players">
              {shortlistRows.length === 0 && <li className="invite-empty">Nothing shortlisted yet — star players to plan your next picks.</li>}
              {shortlistRows.map(({ entry, taken, eligibleNow }) => (
                <li key={entry.id} className={`pool-row${taken ? ' is-taken' : ''}`}>
                  <div className="pool-row-facts">
                    <strong>{entry.name}</strong>
                    <small>
                      {entry.overall} · {entry.positions.join('/')} · {entry.clubName}
                      {taken ? ' · Taken' : !eligibleNow ? ' · Not eligible for this slot' : ''}
                    </small>
                  </div>
                  <div className="pool-row-actions">
                    <button
                      type="button"
                      className="pool-star is-shortlisted"
                      aria-pressed="true"
                      aria-label={`Remove ${entry.name} from shortlist`}
                      onClick={() => shortlist.toggle(entry)}
                    >
                      <Star />
                    </button>
                    <button type="button" className="ghost-button" onClick={() => setDetailCardId(entry.id)} aria-label={`View ${entry.name} details`}>
                      <Info /> Details
                    </button>
                    {isMyTurn && !taken && eligibleNow && (
                      <button type="button" className="ghost-button pool-draft" disabled={busy} onClick={() => setPendingPick({ footballerId: entry.id, name: entry.name, overall: entry.overall, positions: entry.positions, clubName: entry.clubName })} aria-label={`Draft ${entry.name}`}>
                        <Check /> Draft
                      </button>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>

        <aside className="draft-room-side" id="team-rail">
          <TeamRail detail={detail} userId={userId} />
        </aside>
      </div>

      {/* §8.3: the room's own sticky action area replaces the global bottom navigation on iPhone. */}
      <div className="draft-action-bar" role="region" aria-label="Draft room actions">
        <div className="draft-action-status" role="status">
          {isMyTurn ? <strong>Your turn — {slotLabel}</strong> : <strong>{activeTeamName} picking</strong>}
        </div>
        <a className="secondary-button" href="#draft-pool">Players</a>
        <a className="secondary-button" href="#team-rail">Squads</a>
      </div>

      {detailCardId != null && (
        <PlayerDetailSheet
          draftId={summary.id}
          footballerId={detailCardId}
          canDraft={isMyTurn}
          busy={busy}
          onDraft={(pick) => { setDetailCardId(null); setPendingPick(pick) }}
          onClose={() => setDetailCardId(null)}
        />
      )}

      {pendingPick && (
        <PickConfirmSheet
          pick={pendingPick}
          teamName={activeTeamName}
          slotLabel={slotLabel}
          verb="Draft"
          busy={busy}
          onConfirm={() => submitPick(pendingPick)}
          onCancel={() => setPendingPick(null)}
        />
      )}
    </section>
  )
}
