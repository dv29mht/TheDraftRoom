import { useState } from 'react'
import { Share, SquarePlus, MonitorDown, BadgeCheck } from 'lucide-react'
import { useAppLifecycleStore } from '../stores/appLifecycleStore'

/** True when running as an installed app (home screen / dock) rather than a browser tab. */
export function isStandaloneDisplay(): boolean {
  return (
    window.matchMedia('(display-mode: standalone)').matches ||
    // iOS Safari's pre-standard flag for home-screen launches.
    (navigator as Navigator & { standalone?: boolean }).standalone === true
  )
}

/** iPhone/iPad detection (iPadOS 13+ reports itself as a Mac, but keeps multi-touch). */
export function isIosSafariFamily(): boolean {
  const ua = navigator.userAgent
  return /iPhone|iPad|iPod/i.test(ua) || (/Macintosh/i.test(ua) && navigator.maxTouchPoints > 1)
}

/**
 * Install-the-app guidance (PR-22, §12.2). Three paths:
 *  - Chromium captured `beforeinstallprompt` → offer the native prompt in-product.
 *  - iOS Safari never fires it → spell out the Share → Add to Home Screen steps.
 *  - Everything else → point at the browser's own install/menu affordance.
 * Shows a quiet "installed" state when already running standalone (the job is done).
 */
export function InstallGuidance({ heading = 'Install the app' }: { heading?: string }) {
  const installPrompt = useAppLifecycleStore((state) => state.installPrompt)
  const setInstallPrompt = useAppLifecycleStore((state) => state.setInstallPrompt)
  const [installed, setInstalled] = useState(false)

  if (isStandaloneDisplay() || installed) {
    return (
      <div className="install-guidance" role="status">
        <BadgeCheck aria-hidden="true" />
        <p>The Draft Room is installed — launch it from your home screen or dock.</p>
      </div>
    )
  }

  const promptInstall = async () => {
    if (!installPrompt) return
    await installPrompt.prompt()
    const choice = await installPrompt.userChoice
    setInstallPrompt(null)
    if (choice.outcome === 'accepted') setInstalled(true)
  }

  return (
    <section className="install-guidance" aria-labelledby="install-guidance-heading">
      <h3 id="install-guidance-heading">{heading}</h3>
      <p>
        Add The Draft Room to your home screen for full-screen drafting, faster loads, and a
        first-class live-draft experience.
      </p>
      {installPrompt ? (
        <button type="button" className="primary-button compact" onClick={() => void promptInstall()}>
          <MonitorDown aria-hidden="true" /> Install The Draft Room
        </button>
      ) : isIosSafariFamily() ? (
        <ol className="install-steps">
          <li>Open The Draft Room in <strong>Safari</strong>.</li>
          <li>
            Tap the <strong>Share</strong> button <Share aria-hidden="true" /> in the toolbar.
          </li>
          <li>
            Choose <strong>Add to Home Screen</strong> <SquarePlus aria-hidden="true" />, then{' '}
            <strong>Add</strong>.
          </li>
        </ol>
      ) : (
        <p className="install-hint">
          In Chrome or Edge, open the browser menu and choose <strong>Install The Draft Room…</strong>{' '}
          (Safari on Mac: File → Add to Dock).
        </p>
      )}
    </section>
  )
}
