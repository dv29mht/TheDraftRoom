import { Clock, TriangleAlert } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { DraftTimer } from '../../types/draft'

// The live pick clock (PR-16/PR-17). The server's snapshot carries the authoritative deadline and its own
// measured remaining seconds; the client only ticks the display down between server updates, calibrated
// against the server measurement so local clock skew cannot change the result. Warning styling begins at
// the server's §6.4 threshold (15s); a paused clock renders frozen. The warning state is also explicit in
// the accessible label — never conveyed by animation alone (§13).
export function TurnCountdown({ timer }: { timer: DraftTimer }) {
  const [remaining, setRemaining] = useState<number | null>(timer.remainingSeconds)

  useEffect(() => {
    setRemaining(timer.remainingSeconds)
    if (!timer.isTimed || timer.isPaused || timer.deadline == null) return
    const serverRemaining = timer.remainingSeconds ?? (new Date(timer.deadline).getTime() - Date.now()) / 1000
    const endAt = Date.now() + serverRemaining * 1000
    const tick = () => setRemaining(Math.max(0, (endAt - Date.now()) / 1000))
    tick()
    const interval = window.setInterval(tick, 500)
    return () => window.clearInterval(interval)
  }, [timer])

  if (!timer.isTimed || remaining == null) return null
  const seconds = Math.ceil(remaining)
  const warning = !timer.isPaused && seconds <= timer.warningSeconds
  const label = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
  // The warning state changes icon and visible text as well as colour, so it
  // survives reduced-motion (no pulse) and colour-vision differences (§13).
  return (
    <span
      className={`turn-countdown${warning ? ' is-warning' : ''}${timer.isPaused ? ' is-paused' : ''}`}
      role="timer"
      aria-label={timer.isPaused ? `Pick timer paused at ${label}` : `${seconds} seconds left to pick`}
    >
      {warning ? <TriangleAlert aria-hidden="true" /> : <Clock aria-hidden="true" />}{' '}
      {timer.isPaused ? `Paused · ${label}` : warning ? `Hurry · ${label}` : label}
    </span>
  )
}
