import { RefreshCw, WifiOff } from 'lucide-react'
import { useAppLifecycleStore } from '../stores/appLifecycleStore'

/**
 * Global PWA lifecycle states (PR-22, §12.2), mounted above the router so every journey — the
 * sign-in flow included — shows them. Two banners, both live regions:
 *  - offline: connectivity is gone; mutations are blocked at the API client until it returns.
 *  - update: a new shell is waiting (service worker) or the API reported a newer contract —
 *    refresh activates the new version so a cached shell never keeps running against an
 *    incompatible API (§18 risk).
 */
export function AppStatusBanners() {
  const online = useAppLifecycleStore((state) => state.online)
  const updateReady = useAppLifecycleStore((state) => state.updateReady)
  const updateDismissed = useAppLifecycleStore((state) => state.updateDismissed)
  const dismissUpdate = useAppLifecycleStore((state) => state.dismissUpdate)
  const applyUpdate = useAppLifecycleStore((state) => state.applyUpdate)

  const showUpdate = online && updateReady && !updateDismissed
  if (online && !showUpdate) return null

  return (
    <div className="app-banner-stack">
      {!online && (
        <div className="app-banner app-banner-offline" role="status">
          <WifiOff aria-hidden="true" />
          <p>
            <strong>You&rsquo;re offline.</strong> Reading is fine; picks and other actions are
            paused until the connection returns.
          </p>
        </div>
      )}
      {showUpdate && (
        <div className="app-banner app-banner-update" role="status">
          <RefreshCw aria-hidden="true" />
          <p>
            <strong>A new version of The Draft Room is ready.</strong> Refresh to keep everything
            in sync.
          </p>
          <div className="app-banner-actions">
            <button type="button" className="primary-button compact" onClick={applyUpdate}>
              Refresh now
            </button>
            <button type="button" className="secondary-button compact" onClick={dismissUpdate}>
              Later
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
