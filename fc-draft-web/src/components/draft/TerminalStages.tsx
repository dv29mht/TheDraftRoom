import { Ban, Trophy } from 'lucide-react'
import { Link } from 'react-router-dom'
import type { DraftDetail } from '../../types/draft'
import { SquadBoard } from './SquadBoard'

// The terminal cancelled/abandoned stage (PR-16): history is preserved, nothing more can happen.
export function CancelledStage({ detail }: { detail: DraftDetail }) {
  const cancelReason = [...detail.events].reverse()
    .find((evt) => evt.type === 'DraftCancelled' || evt.type === 'DraftAbandoned')?.reason

  return (
    <section className="panel formation-panel">
      <div className="panel-heading"><div><span className="eyebrow">{detail.summary.status}</span><h3>This draft has ended</h3></div><Ban aria-hidden="true" /></div>
      <div className="lobby-banner" role="status">
        <Ban aria-hidden="true" />
        <div>
          <strong>Draft {detail.summary.status.toLowerCase()}{cancelReason ? ` — ${cancelReason}` : ''}</strong>
          <span>The full history is preserved; no further picks or controls are possible.</span>
        </div>
      </div>
      <SquadBoard detail={detail} />
    </section>
  )
}

export function CompletedStage({ detail }: { detail: DraftDetail }) {
  return (
    <section className="panel formation-panel">
      <div className="panel-heading"><div><span className="eyebrow">Draft complete</span><h3>Final squads</h3></div><Trophy aria-hidden="true" /></div>
      <div className="lobby-banner" role="status">
        <Trophy aria-hidden="true" />
        <div><strong>Draft complete</strong><span>Every team has filled all 16 squad slots.</span></div>
      </div>
      <div className="host-actions">
        <Link className="primary-button compact" to={`/drafts/${detail.summary.id}/results`}>
          <Trophy /> View results &amp; archive
        </Link>
      </div>
      <SquadBoard detail={detail} />
    </section>
  )
}
