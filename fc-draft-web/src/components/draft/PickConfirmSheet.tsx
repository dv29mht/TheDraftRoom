import { Check, X } from 'lucide-react'
import { useEffect, useRef } from 'react'

export type PendingPick = {
  footballerId: number
  name: string
  overall: number
  positions: string[]
  clubName?: string | null
}

/**
 * The §9.6 confirmation step: no selection is ever one tap. The sheet names the draft team and the roster
 * slot the pick fills, so a shared 2v2 device always knows which squad it is committing to. Fully
 * keyboard-operable: focus lands on Confirm, Escape cancels, and the two actions loop with Tab.
 */
export function PickConfirmSheet({ pick, teamName, slotLabel, verb, busy, onConfirm, onCancel }: {
  pick: PendingPick
  teamName: string
  slotLabel: string
  verb: 'Draft' | 'Protect'
  busy: boolean
  onConfirm: () => void
  onCancel: () => void
}) {
  const confirmRef = useRef<HTMLButtonElement>(null)
  const cancelRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    confirmRef.current?.focus()
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCancel()
      if (event.key === 'Tab') {
        // Two focusable actions — loop between them so focus never escapes the modal sheet.
        event.preventDefault()
        const next = document.activeElement === confirmRef.current ? cancelRef.current : confirmRef.current
        next?.focus()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onCancel])

  return (
    <div className="confirm-backdrop pick-confirm-backdrop" onClick={onCancel}>
      <div
        className="confirm-dialog pick-confirm-sheet"
        role="dialog"
        aria-modal="true"
        aria-labelledby="pick-confirm-title"
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id="pick-confirm-title">Confirm {verb.toLowerCase()}</h2>
        <p>
          {verb} <strong>{pick.name}</strong> ({pick.overall} · {pick.positions.join('/')}
          {pick.clubName ? ` · ${pick.clubName}` : ''}) to <strong>{teamName}</strong> — <strong>{slotLabel}</strong>.
        </p>
        <div className="confirm-actions">
          <button ref={cancelRef} className="secondary-button" type="button" disabled={busy} onClick={onCancel}>
            <X /> Cancel
          </button>
          <button ref={confirmRef} className="primary-button compact" type="button" disabled={busy} onClick={onConfirm}>
            <Check /> Confirm {verb.toLowerCase()}
          </button>
        </div>
      </div>
    </div>
  )
}
