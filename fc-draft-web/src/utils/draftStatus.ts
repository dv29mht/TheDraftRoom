/** Friendly labels for raw draft status enums, shared by every status pill. */
export function draftStatusLabel(status: string): string {
  switch (status) {
    case 'Lobby': return 'Open lobby'
    case 'TeamFormation': return 'Team formation'
    case 'ReadyCheck': return 'Ready check'
    case 'SpinnerRanking': return 'Spinner ranking'
    case 'ClubSelection': return 'Club selection'
    case 'PositionDraft': return 'Position draft'
    case 'Paused': return 'Paused'
    case 'Completed': return 'Completed'
    case 'Cancelled': return 'Cancelled'
    case 'Abandoned': return 'Abandoned'
    case 'Draft': return 'Draft'
    default: return status
  }
}
