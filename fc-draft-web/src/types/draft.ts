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
  selectedClubName: string | null
  memberUserIds: string[]
}

export type DraftPick = {
  teamId: string
  slotOrder: number
  footballerId: number
  footballerName: string
  footballerOverall: number
  footballerPosition: string | null
  pickedByParticipantId: string | null
}

export type DraftTurn = {
  phase: 'None' | 'ClubSelection' | 'PositionDraft'
  activeTeamId: string | null
  activeTeamName: string | null
  activeTeamMemberUserIds: string[]
  round: number | null
  direction: 'None' | 'Straight' | 'Ascending' | 'Descending'
  activeSlotOrder: number | null
  activeSlotLabel: string | null
  activeSlotPosition: string | null
  slotAcceptsAnyPosition: boolean
}

export type CatalogClub = {
  id: string
  name: string
  league: string
}

export type CatalogFootballer = {
  id: number
  name: string
  overall: number
  clubId: string
  clubName: string
  positions: string[]
}

// The server-authoritative pick clock (PR-16). Everything derives from the persisted turn anchor, so a
// refreshed client computes the same remaining time; the client only ticks down locally from `deadline`
// between server updates. `remainingSeconds` is measured server-side at projection time.
export type DraftTimer = {
  isTimed: boolean
  isPaused: boolean
  pickTimerSeconds: number
  warningSeconds: number
  turnStartedAt: string | null
  deadline: string | null
  remainingSeconds: number | null
  isInWarning: boolean
}

export type DraftBoard = {
  status: DraftStatus
  turn: DraftTurn
  timer: DraftTimer
  isMyTurn: boolean
  availableClubs: CatalogClub[]
  eligibleFootballers: CatalogFootballer[]
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

export type DraftRosterSlot = {
  order: number
  slotType: string
  position: string | null
  label: string
}

export type DraftDetail = {
  summary: DraftSummary
  capacity: LobbyCapacity
  startRequirements: DraftStartRequirements
  participants: LobbyParticipant[]
  teams: DraftTeam[]
  slots: DraftRosterSlot[]
  picks: DraftPick[]
  turn: DraftTurn
  timer: DraftTimer
  events: DraftEvent[]
}

// The live-hub envelope every accepted mutation broadcasts (PR-17). `detail` may be null (the producer
// had only a summary) — the client then refetches the authoritative snapshot over REST.
export type DraftUpdate = {
  draftId: string
  version: number
  eventType: string
  detail: DraftDetail | null
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
