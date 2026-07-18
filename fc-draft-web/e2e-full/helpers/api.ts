// REST driver for the full-stack E2E suites (PR-23). Mirrors the wire shapes the .NET integration
// suite proves (tests/FcDraft.Api.IntegrationTests/TestClient.cs): camelCase JSON, every draft
// mutation carrying the last-seen `expectedVersion`, 409 on a version conflict. The UI suites use
// it sparingly (assertion cross-checks); the resilience suite uses it to fast-forward a draft to
// the stage under test — exactly the create → invite → join → lock → teams → ready → start →
// spinner → open-clubs → club-select → open-positions → pick sequence.

const API = process.env.DRAFT_API_URL ?? 'http://127.0.0.1:5089'

// The deterministic Testing-environment identities: the two always-seeded accounts plus the
// PR-23 demo players (Database__SeedDemoAccounts=true in playwright.fullstack.config.ts).
export const accounts = {
  admin: { email: 'mdevansh@gmail.com', password: 'DraftAdmin@2026', name: 'ROSTR Admin' },
  player: { email: 'player@draftroom.dev', password: 'Player@2026', name: 'Practice Player' },
  player2: { email: 'player2@draftroom.dev', password: 'Player2@2026', name: 'Practice Player Two' },
  player3: { email: 'player3@draftroom.dev', password: 'Player3@2026', name: 'Practice Player Three' },
  player4: { email: 'player4@draftroom.dev', password: 'Player4@2026', name: 'Practice Player Four' },
} as const

export type Account = (typeof accounts)[keyof typeof accounts]

export type Session = {
  token: string
  userId: string
  email: string
  displayName: string
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly body: string,
    method: string,
    path: string,
  ) {
    super(`${method} ${path} -> ${status}: ${body.slice(0, 300)}`)
  }
}

async function request<T>(method: string, path: string, token?: string, body?: unknown): Promise<T> {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await response.text()
  if (!response.ok) {
    throw new ApiError(response.status, text, method, path)
  }
  return (text ? JSON.parse(text) : undefined) as T
}

export async function login(account: Account): Promise<Session> {
  const auth = await request<{
    accessToken: string
    user: { id: string; displayName: string; email: string }
  }>('POST', '/api/auth/login', undefined, { email: account.email, password: account.password })
  return {
    token: auth.accessToken,
    userId: auth.user.id,
    email: auth.user.email,
    displayName: auth.user.displayName,
  }
}

// ---- Draft wire shapes (the subset the suites read) ----

export type DraftDetail = {
  summary: {
    id: string
    name: string
    format: '1v1' | '2v2'
    status: string
    hostUserId: string
    version: number
    pickTimerSeconds: number
  }
  participants: Array<{
    userId: string
    displayName: string
    isHost: boolean
    seed: 'Seed1' | 'Seed2' | null
    status: 'Invited' | 'Joined'
    isReady: boolean
  }>
  teams: Array<{
    id: string
    name: string
    spinnerRank: number | null
    selectedClubId: string | null
    selectedClubName: string | null
    memberUserIds: string[]
  }>
  slots: Array<{ order: number; slotType: string; position: string | null; label: string }>
  picks: Array<{ teamId: string; slotOrder: number; footballerId: number; footballerName: string }>
  events: Array<{ sequence: number; type: string; version: number }>
}

export type DraftBoard = {
  status: string
  isMyTurn: boolean
  turn: {
    phase: string
    activeTeamId: string | null
    activeTeamName: string | null
    activeTeamMemberUserIds: string[]
    activeSlotOrder: number | null
    activeSlotLabel: string | null
  }
  availableClubs: Array<{ id: string; name: string; league: string | null }>
  eligibleFootballers: Array<{ id: number; name: string; overall: number; clubName: string | null }>
}

const version = (detail: DraftDetail) => detail.summary.version

export const getDetail = (session: Session, draftId: string) =>
  request<DraftDetail>('GET', `/api/drafts/${draftId}`, session.token)

export const getBoard = (session: Session, draftId: string, query = '') =>
  request<DraftBoard>('GET', `/api/drafts/${draftId}/board${query}`, session.token)

export const getResults = (session: Session, draftId: string) =>
  request<{ teams: Array<{ name: string; averageOverall: number; picks: unknown[] }> }>(
    'GET', `/api/drafts/${draftId}/results`, session.token)

async function invitableUserIds(host: Session, emails: string[]): Promise<string[]> {
  const users = await request<Array<{ id: string; email: string }>>(
    'GET', '/api/drafts/invitable-users', host.token)
  return emails.map((email) => {
    const match = users.find((user) => user.email.toLowerCase() === email.toLowerCase())
    if (!match) throw new Error(`No invitable user for ${email}`)
    return match.id
  })
}

export async function createDraft(
  host: Session,
  name: string,
  format: '1v1' | '2v2',
  inviteEmails: string[],
): Promise<DraftDetail> {
  const inviteUserIds = await invitableUserIds(host, inviteEmails)
  return request<DraftDetail>('POST', '/api/drafts', host.token, { name, format, inviteUserIds })
}

const mutate = (session: Session, draftId: string, action: string, body: Record<string, unknown>) =>
  request<DraftDetail>('POST', `/api/drafts/${draftId}/${action}`, session.token, body)

export const join = (session: Session, draftId: string, expectedVersion: number) =>
  mutate(session, draftId, 'join', { expectedVersion })

export const lock = (host: Session, draftId: string, expectedVersion: number) =>
  mutate(host, draftId, 'lock', { expectedVersion })

export const assignSeed = (
  host: Session, draftId: string, participantUserId: string, seed: 'Seed1' | 'Seed2', expectedVersion: number,
) => mutate(host, draftId, 'seeds', { participantUserId, seed, expectedVersion })

export const formTeams = (
  host: Session, draftId: string, teams: Array<{ name?: string; memberUserIds: string[] }>, expectedVersion: number,
) => mutate(host, draftId, 'teams', { teams, expectedVersion })

export const setReady = (session: Session, draftId: string, expectedVersion: number, ready = true) =>
  mutate(session, draftId, 'ready', { ready, expectedVersion })

export const readyCheck = (host: Session, draftId: string, expectedVersion: number) =>
  mutate(host, draftId, 'ready-check', { expectedVersion })

export const start = (host: Session, draftId: string, expectedVersion: number) =>
  mutate(host, draftId, 'start', { expectedVersion })

export const spinner = (host: Session, draftId: string, expectedVersion: number) =>
  mutate(host, draftId, 'spinner', { expectedVersion })

export const openClubs = (host: Session, draftId: string, expectedVersion: number) =>
  mutate(host, draftId, 'open-clubs', { expectedVersion })

export const clubSelect = (
  session: Session, draftId: string, clubId: string, footballerId: number, expectedVersion: number,
) => mutate(session, draftId, 'club-select', { clubId, footballerId, expectedVersion })

export const openPositions = (host: Session, draftId: string, expectedVersion: number) =>
  mutate(host, draftId, 'open-positions', { expectedVersion })

export const pick = (session: Session, draftId: string, footballerId: number, expectedVersion: number) =>
  mutate(session, draftId, 'pick', { footballerId, expectedVersion })

export const cancel = (session: Session, draftId: string, reason: string, expectedVersion: number) =>
  mutate(session, draftId, 'cancel', { reason, expectedVersion })

// ---- Composite drivers ----

/** Finds the session belonging to any member of the currently active team. */
export function activeTeamSession(board: DraftBoard, sessions: Session[]): Session {
  const member = sessions.find((session) => board.turn.activeTeamMemberUserIds.includes(session.userId))
  if (!member) {
    throw new Error(`No session for active team ${board.turn.activeTeamName ?? '(none)'}`)
  }
  return member
}

/**
 * Completes the club/protected-player round over REST: in straight spinner order, each active
 * team picks its first available five-star club and protects that club's best available player.
 */
export async function completeClubRound(host: Session, sessions: Session[], draftId: string): Promise<DraftDetail> {
  let detail = await getDetail(host, draftId)
  while (detail.summary.status === 'ClubSelection') {
    const board = await getBoard(host, draftId)
    // The round is complete when no team is on the clock (the host then opens the position draft).
    if (board.turn.phase !== 'ClubSelection' || !board.turn.activeTeamId) break
    const club = board.availableClubs[0]
    if (!club) throw new Error('No available five-star club for the active team')
    const clubBoard = await getBoard(host, draftId, `?clubId=${club.id}`)
    const protectTarget = clubBoard.eligibleFootballers[0]
    if (!protectTarget) throw new Error(`No protectable player at ${club.name}`)
    const actor = activeTeamSession(board, sessions)
    detail = await clubSelect(actor, draftId, club.id, protectTarget.id, version(detail))
  }
  return detail
}

/**
 * Drives a fresh lobby all the way to a LIVE position draft over REST — the documented sequence:
 * create → invite (at creation) → join → lock → teams → ready×N → ready-check → start → spinner →
 * open-clubs → club-select per team → open-positions. For 2v2, participants are paired in the
 * order given: [0,1] (Seed1+Seed2) and [2,3] (Seed1+Seed2).
 */
export async function driveToPositionDraft(
  name: string,
  format: '1v1' | '2v2',
  sessions: Session[],
): Promise<{ draftId: string; detail: DraftDetail; host: Session }> {
  const [host, ...invitees] = sessions
  let detail = await createDraft(host, name, format, invitees.map((session) => session.email))
  const draftId = detail.summary.id

  for (const invitee of invitees) {
    detail = await join(invitee, draftId, version(detail))
  }

  detail = await lock(host, draftId, version(detail))

  if (format === '2v2') {
    const seeds: Array<'Seed1' | 'Seed2'> = ['Seed1', 'Seed2', 'Seed1', 'Seed2']
    for (let i = 0; i < sessions.length; i++) {
      detail = await assignSeed(host, draftId, sessions[i].userId, seeds[i], version(detail))
    }
    detail = await formTeams(host, draftId, [
      { name: 'Team A', memberUserIds: [sessions[0].userId, sessions[1].userId] },
      { name: 'Team B', memberUserIds: [sessions[2].userId, sessions[3].userId] },
    ], version(detail))
  } else {
    // 1v1: solo teams are auto-projected server-side.
    detail = await formTeams(host, draftId, [], version(detail))
  }

  for (const session of sessions) {
    detail = await setReady(session, draftId, version(detail))
  }

  detail = await readyCheck(host, draftId, version(detail))
  detail = await start(host, draftId, version(detail))
  detail = await spinner(host, draftId, version(detail))
  detail = await openClubs(host, draftId, version(detail))
  detail = await completeClubRound(host, sessions, draftId)
  detail = await openPositions(host, draftId, version(detail))

  if (detail.summary.status !== 'PositionDraft') {
    throw new Error(`Expected PositionDraft, got ${detail.summary.status}`)
  }
  return { draftId, detail, host }
}

/** Submits the active team's pick for the first eligible footballer, returning the new detail. */
export async function pickFirstEligible(
  sessions: Session[],
  draftId: string,
  current: DraftDetail,
): Promise<DraftDetail> {
  const anySession = sessions[0]
  const board = await getBoard(anySession, draftId)
  const target = board.eligibleFootballers[0]
  if (!target) throw new Error(`No eligible footballer for ${board.turn.activeSlotLabel}`)
  const actor = activeTeamSession(board, sessions)
  return pick(actor, draftId, target.id, current.summary.version)
}
