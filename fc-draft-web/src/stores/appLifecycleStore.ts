import { create } from 'zustand'

interface AppLifecycleState {
  /** Browser connectivity (PR-22 §12.2): offline blocks mutations and shows the shell banner. */
  online: boolean
  setOnline: (online: boolean) => void
  /**
   * Version-handshake trigger (api.ts calls this when a response's contract header mismatches):
   * nudges the service worker to fetch the new shell in the background. There is no in-app refresh
   * prompt — a fresh shell is picked up on the next natural navigation or reload.
   */
  reportContractMismatch: () => void
}

// Injected by services/appLifecycle.ts once the service worker registers. Kept outside the store
// so components never touch the virtual:pwa-register module (which only exists in real builds).
let checkForSwUpdate: (() => void) | null = null

export function registerServiceWorkerHooks(hooks: { checkForUpdate?: () => void }) {
  checkForSwUpdate = hooks.checkForUpdate ?? checkForSwUpdate
}

export const useAppLifecycleStore = create<AppLifecycleState>((set) => ({
  online: typeof navigator === 'undefined' ? true : navigator.onLine,
  setOnline: (online) => set({ online }),
  reportContractMismatch: () => {
    // Nudge the service worker so the incompatible cached shell is replaced in the background.
    checkForSwUpdate?.()
  }
}))
