import { useEffect, useRef, type RefObject } from 'react'

const FOCUSABLE =
  'button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])'

/**
 * Traps keyboard focus inside `container` while it is mounted: focuses the
 * first focusable element (or `initialFocus`), loops Tab/Shift+Tab, closes on
 * Escape, locks body scroll, and restores focus to the trigger on unmount.
 *
 * `onClose` is read through a ref so the trap initializes ONCE per mount:
 * callers pass inline arrows, and re-running the effect on every render would
 * steal focus back to the dialog's first control mid-interaction — breaking
 * typing in any dialog with a form (PR-21's reason capture surfaced this).
 */
export function useFocusTrap(
  container: RefObject<HTMLElement | null>,
  onClose: () => void,
  initialFocus?: RefObject<HTMLElement | null>
) {
  const closeRef = useRef(onClose)
  useEffect(() => {
    closeRef.current = onClose
  }, [onClose])

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    const target = initialFocus?.current ?? container.current?.querySelector<HTMLElement>(FOCUSABLE)
    target?.focus()

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        closeRef.current()
        return
      }
      if (event.key !== 'Tab') return
      // Query per keypress: dialog content can change while open (async loads).
      const focusable = Array.from(container.current?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? [])
      if (!focusable.length) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => {
      document.body.style.overflow = previousOverflow
      window.removeEventListener('keydown', onKeyDown)
      previouslyFocused?.focus()
    }
  }, [container, initialFocus])
}
