import { API_CONTRACT } from './apiContract'
import {
  registerServiceWorkerHooks,
  useAppLifecycleStore,
  type BeforeInstallPromptEvent
} from '../stores/appLifecycleStore'

/**
 * PWA lifecycle wiring (PR-22, PRD §12.2) — called once from main.tsx. Everything here feeds the
 * app-lifecycle store; components read the store and never touch browser lifecycle APIs directly.
 * This module is the only importer of `virtual:pwa-register` (loaded lazily, production builds
 * only) so the jsdom test environment never has to resolve the virtual module.
 */
export function initAppLifecycle() {
  const store = useAppLifecycleStore

  // ── Connectivity: the shell banner + the api.ts mutation guard read this flag. ──
  store.getState().setOnline(navigator.onLine)
  window.addEventListener('online', () => store.getState().setOnline(true))
  window.addEventListener('offline', () => store.getState().setOnline(false))

  // ── Install guidance: capture Chromium's prompt so the app offers it in-product, after the
  // user has received value (§12.2), instead of the browser interrupting at first visit. ──
  window.addEventListener('beforeinstallprompt', (event) => {
    event.preventDefault()
    store.getState().setInstallPrompt(event as BeforeInstallPromptEvent)
  })
  window.addEventListener('appinstalled', () => store.getState().setInstallPrompt(null))

  // ── iPhone on-screen keyboard (§12.2): expose the keyboard overlap as a CSS custom property so
  // the draft room's sticky action area (and the bottom nav) can stay above it. On browsers where
  // the keyboard resizes the layout viewport the value stays 0 and the CSS is inert. ──
  const viewport = window.visualViewport
  if (viewport) {
    const updateKeyboardInset = () => {
      const inset = Math.max(0, window.innerHeight - viewport.height - viewport.offsetTop)
      document.documentElement.style.setProperty('--keyboard-inset', `${Math.round(inset)}px`)
    }
    viewport.addEventListener('resize', updateKeyboardInset)
    viewport.addEventListener('scroll', updateKeyboardInset)
    updateKeyboardInset()
  }

  // ── Service worker + version handshake (real builds only; dev has no generated sw.js). ──
  if (import.meta.env.PROD && 'serviceWorker' in navigator) {
    void import('virtual:pwa-register').then(({ registerSW }) => {
      let swRegistration: ServiceWorkerRegistration | undefined
      const updateSW = registerSW({
        // A new shell finished downloading and is waiting — the §12.2 refresh prompt.
        onNeedRefresh: () => store.getState().showUpdatePrompt(),
        onRegisteredSW: (_url, registration) => {
          swRegistration = registration
          // A single long-lived tab (a PWA especially) never re-registers on navigation, so poll:
          // hourly, and whenever the app returns to the foreground.
          const check = () => swRegistration?.update().catch(() => undefined)
          setInterval(check, 60 * 60 * 1000)
          document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') {
              void check()
              void verifyApiContract()
            }
          })
        }
      })
      registerServiceWorkerHooks({
        // true = skip waiting and reload this (and every) client onto the new shell.
        applyUpdate: () => void updateSW(true),
        checkForUpdate: () => void swRegistration?.update().catch(() => undefined)
      })
    })

    // Handshake at boot: axios checks the contract header on every response, but a freshly opened
    // stale shell should learn it is stale before the user signs in or touches anything.
    void verifyApiContract()
  }
}

/**
 * Asks the API (never cached: /api is excluded from the service worker and stamped no-store)
 * which contract it serves; a mismatch raises the refresh prompt via the store.
 */
export async function verifyApiContract(): Promise<void> {
  try {
    const response = await fetch('/api/meta/version', { cache: 'no-store' })
    if (!response.ok) return
    const body = (await response.json()) as { contract?: number }
    if (typeof body.contract === 'number' && body.contract !== API_CONTRACT) {
      useAppLifecycleStore.getState().reportContractMismatch()
    }
  } catch {
    // Offline or unreachable — connectivity handling owns this case.
  }
}
