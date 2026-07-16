import { CheckCircle2, RefreshCw, X } from 'lucide-react'
import { useEffect, type ReactNode } from 'react'

/** Shared loading row — always announced to assistive tech via role="status". */
export function LoadingState({ children = 'Loading…', className = '' }: { children?: ReactNode; className?: string }) {
  return (
    <div className={`loading-state ${className}`.trim()} role="status">
      <RefreshCw className="spin" aria-hidden="true" /> {children}
    </div>
  )
}

/** Shared error banner — role="alert" so failures are announced immediately. */
export function ErrorBanner({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <div className={`form-error ${className}`.trim()} role="alert">
      {children}
    </div>
  )
}

/**
 * Success notice that announces politely, can be dismissed, and auto-hides —
 * replaces the permanent success banners that never went away.
 */
export function SuccessBanner({ children, onDismiss, autoHideMs = 6000 }: {
  children: ReactNode
  onDismiss: () => void
  autoHideMs?: number
}) {
  useEffect(() => {
    if (!autoHideMs) return
    const timer = window.setTimeout(onDismiss, autoHideMs)
    return () => window.clearTimeout(timer)
  }, [autoHideMs, onDismiss])

  return (
    <div className="success-banner" role="status">
      <CheckCircle2 aria-hidden="true" />
      <span className="success-banner-text">{children}</span>
      <button type="button" className="banner-dismiss" onClick={onDismiss} aria-label="Dismiss notification">
        <X />
      </button>
    </div>
  )
}
