import { X } from 'lucide-react'
import type { DraftDetail, DraftTeam } from '../../types/draft'

export function TeamRoster({ teams, nameOf, onRemove, busy }: {
  teams: DraftTeam[]
  nameOf: (id: string) => string
  onRemove?: (teamId: string) => void
  busy?: boolean
}) {
  if (teams.length === 0) return <p className="coming-soon-note">No teams formed yet.</p>
  return (
    <ul className="team-roster" aria-label="Formed teams">
      {teams.map((team) => (
        <li key={team.id} className="team-card">
          <div className="team-card-head">
            {team.spinnerRank != null && <span className="spinner-rank">{team.spinnerRank}</span>}
            <strong>{team.name}</strong>
            {onRemove && <button className="ghost-button danger" type="button" disabled={busy} onClick={() => onRemove(team.id)} aria-label={`Remove ${team.name}`}><X /></button>}
          </div>
          <div className="team-members">{team.memberUserIds.map(nameOf).join(' · ')}</div>
        </li>
      ))}
    </ul>
  )
}

export function RequirementSummary({ requirements }: { requirements: DraftDetail['startRequirements'] }) {
  if (requirements.canStart || requirements.blockingReasons.length === 0) return null
  return (
    <ul className="requirement-summary" aria-label="What is still needed">
      {requirements.blockingReasons.map((reason) => <li key={reason}>{reason}</li>)}
    </ul>
  )
}
