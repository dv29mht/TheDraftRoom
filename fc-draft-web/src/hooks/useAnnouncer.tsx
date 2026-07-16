import { useCallback, useRef, useState } from 'react'

/**
 * Screen-reader announcements for live, server-pushed changes (SignalR picks,
 * turn changes, attendance). Render `announcer` once in the component and call
 * `announce(...)` — the visually-hidden polite log reads updates without
 * stealing focus or interrupting the user.
 */
export function useAnnouncer() {
  const [message, setMessage] = useState('')
  const pending = useRef<number | null>(null)

  const announce = useCallback((next: string) => {
    // Clear first so an identical consecutive message is still re-announced.
    setMessage('')
    if (pending.current != null) window.clearTimeout(pending.current)
    pending.current = window.setTimeout(() => setMessage(next), 50)
  }, [])

  const announcer = (
    <div className="sr-only" role="log" aria-live="polite">
      {message}
    </div>
  )

  return { announce, announcer }
}
