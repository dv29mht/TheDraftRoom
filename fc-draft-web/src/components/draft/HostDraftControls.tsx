import { Ban, Pause } from 'lucide-react'
import { useState } from 'react'
import { draftsApi } from '../../services/api'
import type { DraftDetail } from '../../types/draft'
import type { StageMutate } from './common'

// Host pause/cancel with the required reason (PR-16, PRD §9.6). One shared reason field keeps the
// controls minimal; the server rejects a blank reason regardless.
export function HostDraftControls({ detail, busy, mutate }: { detail: DraftDetail; busy: boolean; mutate: StageMutate }) {
  const summary = detail.summary
  const [reason, setReason] = useState('')
  const ready = reason.trim().length > 0

  return (
    <section className="panel host-controls">
      <div className="panel-heading"><div><span className="eyebrow">Host controls</span><h3>Pause or cancel</h3></div><Pause aria-hidden="true" /></div>
      <label className="control-reason">
        <span>Reason (required)</span>
        <input
          type="text"
          value={reason}
          maxLength={512}
          placeholder="Why are you pausing or cancelling?"
          onChange={(event) => setReason(event.target.value)}
          aria-label="Reason for pausing or cancelling"
          disabled={busy}
        />
      </label>
      <div className="host-actions">
        <button
          className="secondary-button"
          type="button"
          disabled={busy || !ready}
          onClick={() => void mutate((version) => draftsApi.pause(summary.id, reason.trim(), version)).then(() => setReason(''))}
        >
          <Pause /> Pause draft
        </button>
        <button
          className="ghost-button danger"
          type="button"
          disabled={busy || !ready}
          onClick={() => void mutate((version) => draftsApi.cancel(summary.id, reason.trim(), version)).then(() => setReason(''))}
        >
          <Ban /> Cancel draft
        </button>
      </div>
    </section>
  )
}
