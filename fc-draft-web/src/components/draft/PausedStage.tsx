import { Pause, Play } from 'lucide-react'
import { draftsApi } from '../../services/api'
import type { DraftDetail } from '../../types/draft'
import type { StageMutate } from './common'
import { useDraftRoomChrome } from './common'
import { SquadBoard } from './SquadBoard'
import { TurnCountdown } from './TurnCountdown'

// The paused stage (PR-16). The clock is frozen server-side (paused time never elapses); the squads stay
// visible so everyone keeps their bearings, and the host resumes exactly where the draft left off. The
// room chrome (no global bottom nav) stays active so a paused live draft still cannot be tabbed out of
// accidentally.
export function PausedStage({ detail, isHost, busy, mutate }: {
  detail: DraftDetail
  isHost: boolean
  busy: boolean
  mutate: StageMutate
}) {
  const summary = detail.summary
  const pauseReason = [...detail.events].reverse().find((evt) => evt.type === 'DraftPaused')?.reason

  useDraftRoomChrome()

  return (
    <section className="panel formation-panel">
      <div className="panel-heading"><div><span className="eyebrow">Paused</span><h2>The draft is paused</h2></div><Pause aria-hidden="true" /></div>
      <div className="lobby-banner" role="status">
        <Pause aria-hidden="true" />
        <div>
          <strong>Draft paused{pauseReason ? ` — ${pauseReason}` : ''}</strong>
          <span>The pick clock is frozen; paused time never counts against the turn.</span>
        </div>
        <TurnCountdown timer={detail.timer} />
      </div>
      {isHost && (
        <div className="host-actions">
          <button className="primary-button compact" type="button" disabled={busy} onClick={() => void mutate((version) => draftsApi.resume(summary.id, version))}>
            <Play /> Resume draft
          </button>
        </div>
      )}
      {!isHost && <p className="coming-soon-note" role="status">Waiting for the host to resume.</p>}
      <SquadBoard detail={detail} />
    </section>
  )
}
