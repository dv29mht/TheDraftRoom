import { create } from 'zustand'

/** The Chromium install prompt event (not yet in lib.dom); Safari never fires it. */
export type BeforeInstallPromptEvent = Event & {
  prompt: () => Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

interface AppLifecycleState {
  /** Browser connectivity (PR-22 §12.2): offline blocks mutations and shows the shell banner. */
  online: boolean
  /**
   * A newer build is ready: the service worker downloaded a new shell (onNeedRefresh) or the API
   * reported a contract number this bundle doesn't match (version handshake).
   */
  updateReady: boolean
  /** "Later" was pressed; the prompt stays away until the next trigger or reload. */
  updateDismissed: boolean
  /** Captured beforeinstallprompt, so install guidance can offer the native prompt. */
  installPrompt: BeforeInstallPromptEvent | null
  setOnline: (online: boolean) => void
  /** Surface the refresh prompt (re-raises it even if previously dismissed). */
  showUpdatePrompt: () => void
  dismissUpdate: () => void
  setInstallPrompt: (event: BeforeInstallPromptEvent | null) => void
  /**
   * Version-handshake trigger (api.ts calls this when a response's contract header mismatches):
   * asks the service worker to fetch the new shell, then prompts.
   */
  reportContractMismatch: () => void
  /** Activate the waiting service worker and reload onto the new shell. */
  applyUpdate: () => void
}

// Injected by services/appLifecycle.ts once the service worker registers. Kept outside the store
// so components never touch the virtual:pwa-register module (which only exists in real builds).
let applySwUpdate: (() => void) | null = null
let checkForSwUpdate: (() => void) | null = null

export function registerServiceWorkerHooks(hooks: {
  applyUpdate?: () => void
  checkForUpdate?: () => void
}) {
  applySwUpdate = hooks.applyUpdate ?? applySwUpdate
  checkForSwUpdate = hooks.checkForUpdate ?? checkForSwUpdate
}

export const useAppLifecycleStore = create<AppLifecycleState>((set, get) => ({
  online: typeof navigator === 'undefined' ? true : navigator.onLine,
  updateReady: false,
  updateDismissed: false,
  installPrompt: null,
  setOnline: (online) => set({ online }),
  showUpdatePrompt: () => set({ updateReady: true, updateDismissed: false }),
  dismissUpdate: () => set({ updateDismissed: true }),
  setInstallPrompt: (installPrompt) => set({ installPrompt }),
  reportContractMismatch: () => {
    // Nudge the service worker so the incompatible cached shell is replaced, not just flagged.
    checkForSwUpdate?.()
    if (!get().updateReady) {
      set({ updateReady: true, updateDismissed: false })
    }
  },
  applyUpdate: () => {
    if (applySwUpdate) {
      applySwUpdate()
    } else {
      window.location.reload()
    }
  }
}))
