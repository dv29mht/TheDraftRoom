import { WifiOff } from 'lucide-react'
import { useAppLifecycleStore } from '../stores/appLifecycleStore'

/**
 * Global PWA lifecycle banner (PR-22, §12.2), mounted above the router so every journey — the
 * sign-in flow included — shows it. One live region:
 *  - offline: connectivity is gone; mutations are blocked at the API client until it returns.
 *
 * The "a new version is ready — refresh" prompt was removed: it nagged on every deploy without a
 * reliable reload, so a fresh shell now simply picks up the new build on the next natural navigation
 * or reload (the service worker still updates in the background).
 */
export function AppStatusBanners() {
  const online = useAppLifecycleStore((state) => state.online)

  if (online) return null

  return (
    <div className="app-banner-stack">
      <div className="app-banner app-banner-offline" role="status">
        <WifiOff aria-hidden="true" />
        <p>
          <strong>You&rsquo;re offline.</strong> Reading is fine; picks and other actions are
          paused until the connection returns.
        </p>
      </div>
    </div>
  )
}
