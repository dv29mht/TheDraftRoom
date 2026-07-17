import { Check, WifiOff, X } from 'lucide-react'
import { useRef } from 'react'
import { Modal } from '../ui/Modal'
import { useAppLifecycleStore } from '../../stores/appLifecycleStore'

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
  // §12.2 (PR-22): while offline the confirm action is explicitly blocked with an explanation —
  // never a submit that dies in a confusing network error mid-draft.
  const online = useAppLifecycleStore((state) => state.online)

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
      {!online && (
        <p className="confirm-offline-note" role="status">
          <WifiOff aria-hidden="true" /> You&rsquo;re offline — reconnect to confirm this {verb.toLowerCase()}.
        </p>
      )}
      <div className="confirm-actions">
        <button className="secondary-button" type="button" disabled={busy} onClick={onCancel}>
          <X /> Cancel
        </button>
        <button
          ref={confirmRef}
          className="primary-button compact"
          type="button"
          disabled={busy || !online}
          onClick={onConfirm}
        >
          <Check /> Confirm {verb.toLowerCase()}
        </button>
      </div>
    </Modal>
  )
}
