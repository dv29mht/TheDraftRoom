export type DraftFormat = '1v1' | '2v2'

export type DraftStatus =
  | 'Draft'
  | 'Lobby'
  | 'TeamFormation'
  | 'ReadyCheck'
  | 'SpinnerRanking'
  | 'ClubSelection'
  | 'PositionDraft'
  | 'Paused'
  | 'Completed'
  | 'Cancelled'
  | 'Abandoned'

export type ParticipantStatus = 'Invited' | 'Joined'

export type DraftSeed = 'Seed1' | 'Seed2'

export type DraftSummary = {
  id: string
  code: string
  name: string
  format: DraftFormat
  status: DraftStatus
  hostUserId: string
  version: number
  pickTimerSeconds: number
  pinnedDatasetVersionId: string | null
  participantCount: number
  createdAt: string
  startedAt: string | null
  completedAt: string | null
}

export type LobbyParticipant = {
  userId: string
  displayName: string | null
  email: string | null
  isHost: boolean
  seed: DraftSeed | null
  status: ParticipantStatus
  isReady: boolean
}

export type DraftTeam = {
  id: string
  name: string
  spinnerRank: number | null
  selectedClubId: string | null
  memberUserIds: string[]
}

export type DraftStartRequirements = {
  teamCount: number
  minTeams: number
  maxTeams: number
  membersPerTeam: number
  allPresent: boolean
  allAssigned: boolean
  teamsValid: boolean
  allReady: boolean
  canBeginReadyCheck: boolean
  canStart: boolean
  blockingReasons: string[]
}

export type LobbyCapacity = {
  min: number
  max: number
  requiresEven: boolean
  participantCount: number
  joinedCount: number
  invitedCount: number
  meetsMinimum: boolean
  withinMaximum: boolean
  meetsEven: boolean
  canLock: boolean
}

export type DraftEvent = {
  sequence: number
  type: string
  fromStatus: string | null
  toStatus: string | null
  version: number
  actorUserId: string | null
  reason: string | null
  createdAt: string
}

export type DraftDetail = {
  summary: DraftSummary
  capacity: LobbyCapacity
  startRequirements: DraftStartRequirements
  participants: LobbyParticipant[]
  teams: DraftTeam[]
  slots: unknown[]
  events: DraftEvent[]
}

export type TeamFormationInput = {
  name?: string | null
  memberUserIds: string[]
}

export type InvitableUser = {
  id: string
  displayName: string
  email: string
}

export type CreateLobbyInput = {
  name: string
  format: DraftFormat
  rosterTemplateId?: string | null
  inviteUserIds?: string[]
}
