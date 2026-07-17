import { Ban, CheckCheck, Inbox, Mail, Megaphone, Trophy, UserPlus } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { meApi } from '../services/api'
import type { UserNotification, UserNotifications } from '../types/draft'

const REFRESH_MS = 60_000

// The player-facing notification centre (PR-20, §9.9): persistent, per-user, unread badge + mark-read,
// deep-linking to the draft. Polls lightly (notifications also arrive whenever the page refetches);
// distinct from the admin-only live activity centre, which stays ephemeral.
export function NotificationCenter() {
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [inbox, setInbox] = useState<UserNotifications>({ items: [], unreadCount: 0 })
  const containerRef = useRef<HTMLDivElement>(null)

  const refresh = useCallback(() => {
    meApi.notifications().then(setInbox).catch(() => { /* non-fatal; the next poll retries */ })
  }, [])

  useEffect(() => {
    refresh()
    const interval = window.setInterval(refresh, REFRESH_MS)
    return () => window.clearInterval(interval)
  }, [refresh])

  useEffect(() => {
    if (!open) return
    refresh()
    const closeOnOutside = (event: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) setOpen(false)
    }
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') setOpen(false) }
    window.addEventListener('mousedown', closeOnOutside)
    window.addEventListener('keydown', closeOnEscape)
    return () => {
      window.removeEventListener('mousedown', closeOnOutside)
      window.removeEventListener('keydown', closeOnEscape)
    }
  }, [open, refresh])

  const openNotification = async (notification: UserNotification) => {
    try {
      if (notification.readAt == null) setInbox(await meApi.markRead(notification.id))
    } catch { /* stale id — the refresh below reconciles */ }
    setOpen(false)
    if (notification.draftId) {
      navigate(notification.type === 'draft.completed'
        ? `/drafts/${notification.draftId}/results`
        : `/drafts/${notification.draftId}`)
    }
  }

  const markAllRead = async () => {
    try { setInbox(await meApi.markAllRead()) } catch { /* non-fatal */ }
  }

  const iconFor = (type: string) => {
    switch (type) {
      case 'draft.invited': return <UserPlus />
      case 'draft.completed': return <Trophy />
      case 'draft.cancelled': return <Ban />
      case 'announcement': return <Megaphone /> // PR-21 admin announcements
      default: return <Mail />
    }
  }

  return (
    <div className="notification-center" ref={containerRef}>
      <button
        className="icon-button notification-trigger"
        aria-label={`${inbox.unreadCount} unread notifications`}
        aria-expanded={open}
        onClick={() => setOpen((current) => !current)}
      >
        <Inbox />
        {inbox.unreadCount > 0 && (
          <span className="notification-count" aria-hidden="true">{Math.min(inbox.unreadCount, 9)}{inbox.unreadCount > 9 ? '+' : ''}</span>
        )}
      </button>
      {/* Badge changes are announced without opening the popover. */}
      <span className="sr-only" aria-live="polite">
        {inbox.unreadCount > 0 ? `${inbox.unreadCount} unread notifications` : ''}
      </span>
      {open && (
        <section className="notification-popover" aria-label="Your notifications">
          <header>
            <div>
              <strong>Notifications</strong>
              {inbox.unreadCount > 0 && (
                <button className="link-button" type="button" onClick={() => void markAllRead()}>
                  <CheckCheck aria-hidden="true" /> Mark all read
                </button>
              )}
            </div>
            <small>Invitations, reminders, and results — kept until you clear them</small>
          </header>
          <div className="notification-list">
            {inbox.items.map((notification) => (
              <article key={notification.id} className={notification.readAt == null ? 'is-unread' : ''}>
                <span className={`notification-icon ${notification.type === 'draft.invited' ? 'room' : ''}`}>
                  {iconFor(notification.type)}
                </span>
                <div>
                  {/* Buttons allow phrasing content only, so body/time render as styled spans. */}
                  <button className="notification-open" type="button" onClick={() => void openNotification(notification)}>
                    <strong>{notification.title}</strong>
                    <span className="notification-body">{notification.body}</span>
                    <span className="notification-time">
                      {new Date(notification.createdAt).toLocaleString([], { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })}
                    </span>
                  </button>
                </div>
              </article>
            ))}
            {!inbox.items.length && (
              <div className="notification-empty">
                <Inbox />
                <strong>Nothing yet</strong>
                <span>Draft invitations, reminders, and results land here.</span>
              </div>
            )}
          </div>
        </section>
      )}
    </div>
  )
}
