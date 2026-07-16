import type { AuthUser } from '../types/auth'
import type { DraftBoard, DraftDetail, DraftStatus, DraftTimer, DraftTurn, LobbyParticipant } from '../types/draft'

// Shared draft test factories (grown from LobbyPage.test.tsx as PR-18 split the stages into components):
// deterministic snapshots for any lobby/draft state, extended per-test through the `over` bags.

export const HOST: AuthUser = { id: 'host-1', displayName: 'Host One', email: 'host@draftroom.dev', role: 'player' }
export const GUEST: AuthUser = { id: 'guest-1', displayName: 'Guest One', email: 'guest@draftroom.dev', role: 'player' }

export const idleTurn: DraftTurn = {
  phase: 'None', activeTeamId: null, activeTeamName: null, activeTeamMemberUserIds: [],
  round: null, direction: 'None', activeSlotOrder: null, activeSlotLabel: null, activeSlotPosition: null, slotAcceptsAnyPosition: false,
}

export const idleTimer: DraftTimer = {
  isTimed: false, isPaused: false, pickTimerSeconds: 120, warningSeconds: 15,
  turnStartedAt: null, deadline: null, remainingSeconds: null, isInWarning: false,
}

export function participant(over: Partial<LobbyParticipant> & { userId: string }): LobbyParticipant {
  return {
    userId: over.userId, displayName: over.displayName ?? over.userId, email: null,
    isHost: over.isHost ?? false, seed: over.seed ?? null, status: over.status ?? 'Joined', isReady: over.isReady ?? false,
  }
}

export function detail(over?: {
  status?: DraftStatus
  hostViewer?: boolean
  participants?: LobbyParticipant[]
  teams?: DraftDetail['teams']
  requirements?: Partial<DraftDetail['startRequirements']>
  capacity?: Partial<DraftDetail['capacity']>
  picks?: DraftDetail['picks']
  slots?: DraftDetail['slots']
  turn?: Partial<DraftTurn>
  timer?: Partial<DraftTimer>
  events?: DraftDetail['events']
  version?: number
}): DraftDetail {
  const status = over?.status ?? 'Lobby'
  return {
    summary: {
      id: 'd1', code: 'ABC123', name: 'Tuesday Draft', format: '1v1', status,
      hostUserId: HOST.id, version: over?.version ?? 3, pickTimerSeconds: 120, pinnedDatasetVersionId: null,
      participantCount: 2, createdAt: '2026-07-15T00:00:00Z', startedAt: null, completedAt: null,
    },
    capacity: {
      min: 2, max: 10, requiresEven: false, participantCount: 2, joinedCount: 2, invitedCount: 0,
      meetsMinimum: true, withinMaximum: true, meetsEven: true, canLock: true, ...over?.capacity,
    },
    startRequirements: {
      teamCount: over?.teams?.length ?? 0, minTeams: 2, maxTeams: 10, membersPerTeam: 1,
      allPresent: true, allAssigned: false, teamsValid: false, allReady: false,
      canBeginReadyCheck: false, canStart: false, blockingReasons: [], ...over?.requirements,
    },
    participants: over?.participants ?? [
      participant({ userId: HOST.id, displayName: 'Host One', isHost: true }),
      participant({ userId: GUEST.id, displayName: 'Guest One', status: 'Invited' }),
    ],
    teams: over?.teams ?? [],
    slots: over?.slots ?? [],
    picks: over?.picks ?? [],
    turn: { ...idleTurn, ...over?.turn },
    timer: { ...idleTimer, ...over?.timer },
    events: over?.events ?? [],
  }
}

export function board(over?: Partial<DraftBoard>): DraftBoard {
  return {
    status: 'ClubSelection', turn: idleTurn, timer: idleTimer, isMyTurn: false, availableClubs: [], eligibleFootballers: [], ...over,
  }
}

/** A running position-draft clock: anchored now, `remaining` seconds left. */
export function runningTimer(remaining: number, over?: Partial<DraftTimer>): DraftTimer {
  return {
    ...idleTimer,
    isTimed: true,
    turnStartedAt: new Date(Date.now() - (120 - remaining) * 1000).toISOString(),
    deadline: new Date(Date.now() + remaining * 1000).toISOString(),
    remainingSeconds: remaining,
    isInWarning: remaining <= 15,
    ...over,
  }
}
