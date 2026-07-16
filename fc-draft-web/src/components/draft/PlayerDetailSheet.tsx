import { Ban, Check, RefreshCw, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { draftsApi } from '../../services/api'
import type { DraftFootballerCard } from '../../types/draft'
import { Modal } from '../ui/Modal'
import type { PendingPick } from './PickConfirmSheet'

/**
 * The full §9.6 player card, read on demand from the draft's PINNED dataset (never the active one):
 * name, overall, card stats, primary/alternate positions, roles with +/++ familiarity, PlayStyles, and
 * club/league/nation. When the footballer is already held or drafted the sheet says by whom and into
 * which slot, so an unavailable player is understandable rather than silently missing from the pool.
 */
export function PlayerDetailSheet({ draftId, footballerId, canDraft, busy, onDraft, onClose }: {
  draftId: string
  footballerId: number
  canDraft: boolean
  busy: boolean
  onDraft: (pick: PendingPick) => void
  onClose: () => void
}) {
  const [card, setCard] = useState<DraftFootballerCard | null>(null)
  const [failed, setFailed] = useState(false)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let active = true
    setCard(null)
    setFailed(false)
    draftsApi.footballerCard(draftId, footballerId)
      .then((next) => { if (active) setCard(next) })
      .catch(() => { if (active) setFailed(true) })
    return () => { active = false }
  }, [draftId, footballerId, attempt])

  const familiarity = (level: number) => (level >= 2 ? '++' : level === 1 ? '+' : '')

  return (
    <Modal
      onClose={onClose}
      labelledBy="player-sheet-title"
      backdropClassName="confirm-backdrop player-sheet-backdrop"
      dialogClassName="player-sheet"
    >
        <button className="icon-button player-sheet-close" type="button" onClick={onClose} aria-label="Close player card">
          <X />
        </button>

        {!card && !failed && <div className="loading-state" role="status"><RefreshCw className="spin" aria-hidden="true" /> Loading player card…</div>}
        {failed && (
          <div className="form-error" role="alert">
            Could not load this player card.{' '}
            <button className="link-button" type="button" onClick={() => setAttempt((current) => current + 1)}>
              <RefreshCw /> Try again
            </button>
          </div>
        )}

        {card && (
          <>
            <header className="player-sheet-hero">
              <span className="overall">{card.card.overall}<small>OVR</small></span>
              <div>
                <h2 id="player-sheet-title">{card.card.name}</h2>
                <p>{card.card.clubName} · {card.card.league} · {card.card.nation}</p>
                <div className="position-list">
                  {card.card.positions.map((position, index) => (
                    <span key={position} className={index === 0 ? 'position-primary' : ''}>
                      {position}
                      <small>{index === 0 ? 'Primary' : 'Alternate'}</small>
                    </span>
                  ))}
                </div>
              </div>
            </header>

            {card.isTaken && (
              <div className="lobby-banner taken-banner" role="status">
                <Ban aria-hidden="true" />
                <div>
                  <strong>Unavailable — already {card.takenSlotLabel === 'Held player' ? 'protected' : 'drafted'}</strong>
                  <span>{card.takenByTeamName ?? 'Another team'} holds {card.card.name}{card.takenSlotLabel ? ` (${card.takenSlotLabel})` : ''}.</span>
                </div>
              </div>
            )}

            {card.card.stats.length > 0 && (
              <div className="detail-stat-grid" aria-label="Card stats">
                {card.card.stats.map((stat) => <span key={stat.label}><strong>{stat.value}</strong>{stat.label}</span>)}
              </div>
            )}

            {card.card.roles.length > 0 && (
              <>
                <h3>Roles</h3>
                <div className="role-list">
                  {card.card.roles.map((role) => (
                    <span key={`${role.position}-${role.name}`}>
                      <b>{role.position}</b> {role.name}
                      {familiarity(role.familiarity) && <strong>{familiarity(role.familiarity)}</strong>}
                    </span>
                  ))}
                </div>
              </>
            )}

            {card.card.playStyles.length > 0 && (
              <>
                <h3>PlayStyles</h3>
                <div className="playstyle-list">
                  {card.card.playStyles.map((style) => (
                    <span key={style.name} className={style.plus ? 'plus' : ''}>
                      {style.name}{style.plus && <b>+</b>}
                    </span>
                  ))}
                </div>
              </>
            )}

            {canDraft && !card.isTaken && (
              <div className="confirm-actions">
                <button
                  className="primary-button compact"
                  type="button"
                  disabled={busy}
                  onClick={() => onDraft({
                    footballerId: card.card.id,
                    name: card.card.name,
                    overall: card.card.overall,
                    positions: card.card.positions,
                    clubName: card.card.clubName,
                  })}
                >
                  <Check /> Draft {card.card.name}
                </button>
              </div>
            )}
          </>
        )}
    </Modal>
  )
}
