import { useRef, type ReactNode, type RefObject } from 'react'
import { useFocusTrap } from '../../hooks/useFocusTrap'

/**
 * The one dialog shell: focus trap + restore, Escape, backdrop close,
 * aria-modal semantics. Visuals come from the existing CSS classes passed in
 * (`confirm-dialog`, `player-modal`, `player-sheet`, `edit-dialog`, …).
 */
export function Modal({ onClose, labelledBy, backdropClassName = 'confirm-backdrop', dialogClassName = 'confirm-dialog', initialFocus, children }: {
  onClose: () => void
  labelledBy: string
  backdropClassName?: string
  dialogClassName?: string
  initialFocus?: RefObject<HTMLElement | null>
  children: ReactNode
}) {
  const dialog = useRef<HTMLDivElement>(null)
  useFocusTrap(dialog, onClose, initialFocus)

  return (
    <div
      className={backdropClassName}
      role="presentation"
      onMouseDown={(event) => event.target === event.currentTarget && onClose()}
    >
      <div ref={dialog} className={dialogClassName} role="dialog" aria-modal="true" aria-labelledby={labelledBy}>
        {children}
      </div>
    </div>
  )
}
