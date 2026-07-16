import { Check, X } from 'lucide-react'
import { useRef } from 'react'
import { Modal } from '../ui/Modal'

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
 * keyboard-operable via the shared Modal: focus lands on Confirm, Escape cancels, Tab is trapped, and
 * focus returns to the triggering control on close.
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

  return (
    <Modal
      onClose={onCancel}
      labelledBy="pick-confirm-title"
      backdropClassName="confirm-backdrop pick-confirm-backdrop"
      dialogClassName="confirm-dialog pick-confirm-sheet"
      initialFocus={confirmRef}
    >
      <h2 id="pick-confirm-title">Confirm {verb.toLowerCase()}</h2>
      <p>
        {verb} <strong>{pick.name}</strong> ({pick.overall} · {pick.positions.join('/')}
        {pick.clubName ? ` · ${pick.clubName}` : ''}) to <strong>{teamName}</strong> — <strong>{slotLabel}</strong>.
      </p>
      <div className="confirm-actions">
        <button className="secondary-button" type="button" disabled={busy} onClick={onCancel}>
          <X /> Cancel
        </button>
        <button ref={confirmRef} className="primary-button compact" type="button" disabled={busy} onClick={onConfirm}>
          <Check /> Confirm {verb.toLowerCase()}
        </button>
      </div>
    </Modal>
  )
}
