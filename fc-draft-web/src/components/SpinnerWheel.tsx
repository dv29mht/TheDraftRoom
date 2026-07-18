import { useEffect, useMemo, useState } from 'react'
import type { DraftTeam } from '../types/draft'

// MASTER.md palette only — brand pink/magenta/violet plus the semantic ramp; no legacy lime.
const WHEEL_COLORS = ['#d4af37', '#0a0a0a', '#e6d7a6', '#8a6a10', '#c9a227', '#1a1a1a', '#f2e8c6', '#b8860b']

function prefersReducedMotion(): boolean {
  return typeof window !== 'undefined'
    && typeof window.matchMedia === 'function'
    && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

/**
 * The spinner-ranking reveal (PR-13). The committed order is the server-authoritative
 * `teams.spinnerRank`; the wheel is a purely decorative animation over that result and can never change it.
 * The ordered results list is always rendered as the reduced-motion-safe equivalent, so the outcome is
 * explicit without any animation.
 */
export function SpinnerWheel({ teams, spinning }: { teams: DraftTeam[]; spinning: boolean }) {
  const committed = teams.length > 0 && teams.every((team) => team.spinnerRank != null)
  const reducedMotion = prefersReducedMotion()
  const [revealed, setRevealed] = useState(false)

  const ordered = useMemo(
    () => [...teams].sort((a, b) => (a.spinnerRank ?? Number.MAX_SAFE_INTEGER) - (b.spinnerRank ?? Number.MAX_SAFE_INTEGER)),
    [teams],
  )

  // After a commit, hold the wheel animation briefly before revealing the list (skipped for reduced motion).
  useEffect(() => {
    if (!committed) { setRevealed(false); return }
    if (reducedMotion) { setRevealed(true); return }
    setRevealed(false)
    const timer = setTimeout(() => setRevealed(true), 2200)
    return () => clearTimeout(timer)
  }, [committed, reducedMotion])

  const showList = committed && (revealed || reducedMotion)
  const wheelStyle = {
    background: `conic-gradient(${teams
      .map((team, index) => {
        const color = WHEEL_COLORS[index % WHEEL_COLORS.length]
        const from = (index / teams.length) * 360
        const to = ((index + 1) / teams.length) * 360
        return `${color} ${from}deg ${to}deg`
      })
      .join(', ')})`,
  }

  return (
    <div className="spinner-stage">
      {!reducedMotion && (
        <div className={`spinner-wheel${spinning ? ' is-spinning' : ''}${committed && !showList ? ' is-settling' : ''}`} style={wheelStyle} aria-hidden="true">
          <span className="spinner-hub" />
          <span className="spinner-pointer" />
        </div>
      )}

      {showList ? (
        <ol className="spinner-order" aria-label="Committed spinner order">
          {ordered.map((team) => (
            <li key={team.id} className="spinner-order-row">
              <span className="spinner-rank">{team.spinnerRank}</span>
              <strong>{team.name}</strong>
            </li>
          ))}
        </ol>
      ) : (
        <p className="spinner-status" role="status">
          {committed ? 'Revealing the committed order…' : spinning ? 'Spinning…' : 'Ready to spin.'}
        </p>
      )}
    </div>
  )
}
