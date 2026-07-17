import {
  Bell,
  ChevronRight,
  CircleUserRound,
  ClipboardList,
  DraftingCompass,
  DoorOpen,
  Gauge,
  Home,
  LayoutDashboard,
  LogOut,
  Megaphone,
  Menu,
  Moon,
  PanelLeftClose,
  PanelLeftOpen,
  ScrollText,
  Settings,
  Trophy,
  Sun,
  UserPlus,
  UserRoundCog,
  UsersRound,
  X,
  type LucideIcon
} from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'
import { applyTheme, useThemeStore } from '../stores/themeStore'
import { notificationsApi } from '../services/api'
import type { AdminNotification } from '../types/admin'
import { BrandMark } from './BrandMark'
import { NotificationCenter } from './NotificationCenter'

type NavItem = { to: string; label: string; icon: LucideIcon; comingSoon?: boolean }

const primaryLinks: NavItem[] = [
  { to: '/', label: 'Home', icon: Home },
  { to: '/drafts', label: 'Drafts', icon: DraftingCompass },
  { to: '/teams', label: 'Teams', icon: Trophy },
  { to: '/players', label: 'Players', icon: UsersRound },
  { to: '/profile', label: 'Profile', icon: CircleUserRound }
]

// §8.2 admin modules. The Overview dashboard shipped in PR-24.
const adminLinks: NavItem[] = [
  { to: '/admin/overview', label: 'Overview', icon: Gauge },
  { to: '/admin/users', label: 'User management', icon: UserRoundCog },
  { to: '/admin/drafts', label: 'Draft operations', icon: LayoutDashboard },
  { to: '/admin/player-data', label: 'Player data', icon: UsersRound },
  { to: '/admin/templates', label: 'Templates', icon: ClipboardList },
  { to: '/admin/communications', label: 'Communications', icon: Megaphone },
  { to: '/admin/audit-log', label: 'Audit log', icon: ScrollText },
  { to: '/admin/settings', label: 'Settings', icon: Settings }
]

export function AppShell() {
  const [menuOpen, setMenuOpen] = useState(false)
  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => localStorage.getItem('fc-draft-sidebar-collapsed') === 'true')
  const [notificationsOpen, setNotificationsOpen] = useState(false)
  const [notifications, setNotifications] = useState<AdminNotification[]>([])
  const [unreadCount, setUnreadCount] = useState(0)
  const [streamConnected, setStreamConnected] = useState(false)
  const mobileCloseButton = useRef<HTMLButtonElement>(null)
  const user = useAuthStore((state) => state.user)
  const logout = useAuthStore((state) => state.logout)
  const theme = useThemeStore((state) => state.theme)
  const toggleTheme = useThemeStore((state) => state.toggleTheme)
  const location = useLocation()
  const navigate = useNavigate()
  const navigationGroups = [
    { label: 'Draft room', links: primaryLinks },
    ...(user?.role === 'admin' ? [{ label: 'Administration', links: adminLinks }] : [])
  ]
  const allLinks = navigationGroups.flatMap((group) => group.links)
  const currentLink = allLinks.find(({ to }) => location.pathname === to)
    ?? allLinks.find(({ to }) => to !== '/' && location.pathname.startsWith(`${to}/`))
  const moduleTitle = currentLink?.label
    ?? (location.pathname === '/new-lobby' ? 'New draft' : 'The Draft Room')
  const workspaceTitle = 'Draft room'

  useEffect(() => {
    applyTheme(theme)
  }, [theme])

  useEffect(() => {
    localStorage.setItem('fc-draft-sidebar-collapsed', String(sidebarCollapsed))
  }, [sidebarCollapsed])

  useEffect(() => {
    setMenuOpen(false)
    setNotificationsOpen(false)
  }, [location.pathname])

  useEffect(() => {
    if (!menuOpen) return
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    mobileCloseButton.current?.focus()
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setMenuOpen(false)
    }
    const closeAtDesktopWidth = () => {
      if (window.innerWidth > 1023) setMenuOpen(false)
    }
    window.addEventListener('keydown', closeOnEscape)
    window.addEventListener('resize', closeAtDesktopWidth)
    return () => {
      document.body.style.overflow = previousOverflow
      window.removeEventListener('keydown', closeOnEscape)
      window.removeEventListener('resize', closeAtDesktopWidth)
    }
  }, [menuOpen])

  useEffect(() => {
    if (user?.role !== 'admin') return
    const controller = new AbortController()
    let active = true

    const connect = async () => {
      try {
        setNotifications(await notificationsApi.list())
      } catch { /* The stream retry below also recovers the initial list. */ }

      while (active) {
        try {
          const token = localStorage.getItem('fc-draft-token')
          const response = await fetch('/api/notifications/stream', {
            headers: token ? { Authorization: `Bearer ${token}` } : {},
            signal: controller.signal
          })
          if (!response.ok || !response.body) throw new Error('Notification stream unavailable')
          setStreamConnected(true)
          const reader = response.body.getReader()
          const decoder = new TextDecoder()
          let buffer = ''
          while (active) {
            const { value, done } = await reader.read()
            if (done) break
            buffer += decoder.decode(value, { stream: true })
            const events = buffer.split('\n\n')
            buffer = events.pop() ?? ''
            for (const event of events) {
              const data = event.split('\n').find((line) => line.startsWith('data: '))?.slice(6)
              if (!data) continue
              const notification = JSON.parse(data) as AdminNotification
              setNotifications((current) => [notification, ...current.filter((item) => item.id !== notification.id)].slice(0, 30))
              setUnreadCount((count) => count + 1)
            }
          }
        } catch {
          if (active) setStreamConnected(false)
        }
        if (active) await new Promise((resolve) => window.setTimeout(resolve, 1500))
      }
    }

    void connect()
    return () => { active = false; controller.abort() }
  }, [user?.role])

  const signOut = () => {
    logout()
    navigate('/login')
  }

  return (
    <div className={`app-shell ${sidebarCollapsed ? 'sidebar-collapsed' : ''}`}>
      <a className="skip-link" href="#main-content">Skip to main content</a>
      <aside id="primary-sidebar" className={`sidebar ${menuOpen ? 'is-open' : ''} ${sidebarCollapsed ? 'is-collapsed' : ''}`}>
        <div className="sidebar-top">
          <BrandMark />
          <button
            className="icon-button desktop-only sidebar-collapse"
            type="button"
            onClick={() => setSidebarCollapsed((collapsed) => !collapsed)}
            aria-controls="primary-sidebar"
            aria-expanded={!sidebarCollapsed}
            aria-label={sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
            title={sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          >
            {sidebarCollapsed ? <PanelLeftOpen /> : <PanelLeftClose />}
          </button>
          <button ref={mobileCloseButton} className="icon-button mobile-only" onClick={() => setMenuOpen(false)} aria-label="Close navigation">
            <X />
          </button>
        </div>
        <nav className="sidebar-nav" aria-label="Primary navigation">
          {navigationGroups.map((group) => (
            <div className="sidebar-nav-group" key={group.label}>
              <span className="nav-eyebrow">{group.label}</span>
              {group.links.map(({ to, label, icon: Icon, comingSoon }) => comingSoon ? (
                // §12.4: a deferred module is visible and disabled, never absent or seemingly live.
                <span key={to} className="nav-link-disabled" aria-disabled="true" title={`${label} — coming soon`}>
                  <Icon aria-hidden="true" />
                  <span>{label}</span>
                  <span className="coming-soon-pill">Soon</span>
                </span>
              ) : (
                <NavLink key={to} to={to} end={to === '/'} onClick={() => setMenuOpen(false)} title={sidebarCollapsed ? label : undefined}>
                  <Icon aria-hidden="true" />
                  <span>{label}</span>
                  <ChevronRight className="nav-chevron" aria-hidden="true" />
                </NavLink>
              ))}
            </div>
          ))}
        </nav>
        <div className="sidebar-user sidebar-account">
          <span className="avatar" aria-hidden="true">{user?.displayName.slice(0, 2).toUpperCase()}</span>
          <span><strong>{user?.displayName}</strong><small>{user?.role}</small></span>
        </div>
      </aside>

      {menuOpen && <button className="sidebar-scrim" aria-label="Close navigation" onClick={() => setMenuOpen(false)} />}

      <main className="main-stage" id="main-content" tabIndex={-1}>
        <header className="topbar">
          <button className="icon-button mobile-only" onClick={() => setMenuOpen(true)} aria-label="Open navigation" aria-controls="primary-sidebar" aria-expanded={menuOpen}><Menu /></button>
          <nav className="module-breadcrumb" aria-label="Current module">
            <ol>
              <li>{workspaceTitle}</li>
              <li aria-hidden="true"><ChevronRight /></li>
              {/* Breadcrumbs are navigation — each page owns its single <h1>. */}
              <li aria-current="page"><span className="breadcrumb-current">{moduleTitle}</span></li>
            </ol>
          </nav>
          <div className="topbar-actions">
            {location.pathname === '/admin/users' && (
              <Link className="topbar-primary-action" to="/admin/users?invite=1">
                <UserPlus aria-hidden="true" /><span>Add user</span>
              </Link>
            )}
            <button className="icon-button" onClick={toggleTheme} aria-label={`Switch to ${theme === 'light' ? 'dark' : 'light'} mode`} title={`Switch to ${theme === 'light' ? 'dark' : 'light'} mode`}>
              {theme === 'light' ? <Moon /> : <Sun />}
            </button>
            {/* Personal, persistent notifications (PR-20) — every signed-in user. */}
            <NotificationCenter />
            {user?.role === 'admin' && <div className="notification-center">
              <button className="icon-button notification-trigger" aria-label={`${unreadCount} unread notifications`} aria-expanded={notificationsOpen} onClick={() => { setNotificationsOpen((open) => !open); setUnreadCount(0) }}>
                <Bell />{unreadCount > 0 && <span className="notification-count" aria-hidden="true">{Math.min(unreadCount, 9)}{unreadCount > 9 ? '+' : ''}</span>}
              </button>
              <span className="sr-only" aria-live="polite">{unreadCount > 0 ? `${unreadCount} unread admin notifications` : ''}</span>
              {notificationsOpen && <section className="notification-popover" aria-label="Admin notifications">
                <header><div><strong>Live activity</strong><span className={streamConnected ? 'connected' : ''}><i />{streamConnected ? 'Live' : 'Reconnecting'}</span></div><small>Player joins and new draft lobbies</small></header>
                <div className="notification-list">
                  {notifications.map((notification) => <article key={notification.id}>
                    <span className={`notification-icon ${notification.type === 'draft.created' ? 'room' : ''}`}>{notification.type === 'draft.created' ? <DoorOpen /> : <UsersRound />}</span>
                    <div><strong>{notification.title}</strong><p>{notification.message}</p><time dateTime={notification.createdAt}>{new Date(notification.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</time></div>
                  </article>)}
                  {!notifications.length && <div className="notification-empty"><Bell /><strong>No activity yet</strong><span>New events will appear here instantly.</span></div>}
                </div>
              </section>}
            </div>}
            <button className="topbar-signout" type="button" onClick={signOut} aria-label="Sign out" title="Sign out"><LogOut /><span>Sign out</span></button>
          </div>
        </header>
        <div className="page-container"><Outlet /></div>
      </main>

      <nav className="bottom-nav" aria-label="Mobile navigation">
        {primaryLinks.map(({ to, label, icon: Icon }) => (
          <NavLink key={to} to={to} end={to === '/'}>
            <Icon aria-hidden="true" /><span>{label}</span>
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
