import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { LobbyPage } from './LobbyPage'
import { draftsApi } from '../services/api'
import { useAuthStore } from '../stores/authStore'
import type { AuthUser } from '../types/auth'
import type { DraftBoard, DraftDetail, DraftStatus, DraftTeam, DraftTurn, LobbyParticipant } from '../types/draft'

const idleTurn: DraftTurn = {
  phase: 'None', activeTeamId: null, activeTeamName: null, activeTeamMemberUserIds: [],
  round: null, direction: 'None', activeSlotOrder: null, activeSlotLabel: null, activeSlotPosition: null, slotAcceptsAnyPosition: false,
}

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return {
    ...actual,
    draftsApi: {
      ...actual.draftsApi,
      get: vi.fn(),
      invitableUsers: vi.fn(),
      join: vi.fn(),
      lock: vi.fn(),
      removeParticipant: vi.fn(),
      assignSeed: vi.fn(),
      formTeams: vi.fn(),
      setReady: vi.fn(),
      beginReadyCheck: vi.fn(),
      reopenTeams: vi.fn(),
      start: vi.fn(),
      commitSpinner: vi.fn(),
      openClubSelection: vi.fn(),
      selectClubAndProtect: vi.fn(),
      openPositionDraft: vi.fn(),
      submitPick: vi.fn(),
      board: vi.fn(),
    },
  }
})

const getMock = draftsApi.get as unknown as Mock
const joinMock = draftsApi.join as unknown as Mock
const invitableMock = draftsApi.invitableUsers as unknown as Mock
const formTeamsMock = draftsApi.formTeams as unknown as Mock
const assignSeedMock = draftsApi.assignSeed as unknown as Mock
const setReadyMock = draftsApi.setReady as unknown as Mock
const commitSpinnerMock = draftsApi.commitSpinner as unknown as Mock
const boardMock = draftsApi.board as unknown as Mock
const selectClubMock = draftsApi.selectClubAndProtect as unknown as Mock
const submitPickMock = draftsApi.submitPick as unknown as Mock

const HOST: AuthUser = { id: 'host-1', displayName: 'Host One', email: 'host@draftroom.dev', role: 'player' }
const GUEST: AuthUser = { id: 'guest-1', displayName: 'Guest One', email: 'guest@draftroom.dev', role: 'player' }

function participant(over: Partial<LobbyParticipant> & { userId: string }): LobbyParticipant {
  return {
    userId: over.userId, displayName: over.displayName ?? over.userId, email: null,
    isHost: over.isHost ?? false, seed: over.seed ?? null, status: over.status ?? 'Joined', isReady: over.isReady ?? false,
  }
}

function detail(over?: {
  status?: DraftStatus
  hostViewer?: boolean
  participants?: LobbyParticipant[]
  teams?: DraftTeam[]
  requirements?: Partial<DraftDetail['startRequirements']>
  capacity?: Partial<DraftDetail['capacity']>
  picks?: DraftDetail['picks']
  slots?: DraftDetail['slots']
  turn?: Partial<DraftTurn>
}): DraftDetail {
  const status = over?.status ?? 'Lobby'
  return {
    summary: {
      id: 'd1', code: 'ABC123', name: 'Tuesday Draft', format: '1v1', status,
      hostUserId: HOST.id, version: 3, pickTimerSeconds: 120, pinnedDatasetVersionId: null,
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
    events: [],
  }
}

function renderLobby(user: AuthUser) {
  useAuthStore.setState({ user, accessToken: 't', mustChangePassword: false })
  return render(
    <MemoryRouter initialEntries={['/drafts/d1']}>
      <Routes><Route path="/drafts/:draftId" element={<LobbyPage />} /></Routes>
    </MemoryRouter>,
  )
}

function board(over?: Partial<DraftBoard>): DraftBoard {
  return {
    status: 'ClubSelection', turn: idleTurn, isMyTurn: false, availableClubs: [], eligibleFootballers: [], ...over,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  invitableMock.mockResolvedValue([])
  boardMock.mockResolvedValue(board())
})

describe('LobbyPage — open lobby', () => {
  it('shows the roster and an enabled lock control for the host when capacity is met', async () => {
    getMock.mockResolvedValue(detail())
    renderLobby(HOST)

    expect(await screen.findByText('Host One')).toBeInTheDocument()
    expect(screen.getByText('Guest One')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /lock lobby/i })).toBeEnabled()
    // Start now lives in the ready check, so an open lobby shows no Start control.
    expect(screen.queryByRole('button', { name: /start draft/i })).not.toBeInTheDocument()
  })

  it('disables the lock control below the minimum', async () => {
    getMock.mockResolvedValue(detail({ capacity: { canLock: false, meetsMinimum: false, participantCount: 1 } }))
    renderLobby(HOST)

    await screen.findByText('Host One')
    expect(screen.getByRole('button', { name: /lock lobby/i })).toBeDisabled()
  })

  it('lets an invited participant confirm presence', async () => {
    getMock.mockResolvedValue(detail({ participants: [
      participant({ userId: HOST.id, displayName: 'Host One', isHost: true }),
      participant({ userId: GUEST.id, displayName: 'Guest One', status: 'Invited' }),
    ] }))
    joinMock.mockResolvedValue(detail())
    renderLobby(GUEST)

    const confirm = await screen.findByRole('button', { name: /confirm presence/i })
    await userEvent.click(confirm)

    await waitFor(() => expect(joinMock).toHaveBeenCalledWith('d1', 3))
  })

  it('shows an unavailable state for a non-participant (404)', async () => {
    getMock.mockRejectedValue({ response: { status: 404 } })
    renderLobby(GUEST)

    expect(await screen.findByText(/lobby unavailable/i)).toBeInTheDocument()
  })
})

describe('LobbyPage — team formation', () => {
  it('lets the host form solo teams in a 1v1', async () => {
    getMock.mockResolvedValue(detail({ status: 'TeamFormation' }))
    formTeamsMock.mockResolvedValue(detail({ status: 'TeamFormation', teams: [
      { id: 't1', name: 'Host One', spinnerRank: null, selectedClubId: null, selectedClubName: null, memberUserIds: [HOST.id] },
    ] }))
    renderLobby(HOST)

    const form = await screen.findByRole('button', { name: /form solo teams/i })
    await userEvent.click(form)

    await waitFor(() => expect(formTeamsMock).toHaveBeenCalledWith('d1', null, 3))
  })

  it('lets the host assign a seed in a 2v2', async () => {
    const twoVsTwo = detail({ status: 'TeamFormation' })
    twoVsTwo.summary.format = '2v2'
    getMock.mockResolvedValue(twoVsTwo)
    assignSeedMock.mockResolvedValue(twoVsTwo)
    renderLobby(HOST)

    const seedButtons = await screen.findAllByRole('button', { name: 'Seed 1' })
    await userEvent.click(seedButtons[0])

    await waitFor(() => expect(assignSeedMock).toHaveBeenCalledWith('d1', HOST.id, 'Seed1', 3))
  })
})

describe('LobbyPage — ready check', () => {
  const readyDetail = (over?: Partial<DraftDetail['startRequirements']>) => detail({
    status: 'ReadyCheck',
    teams: [
      { id: 't1', name: 'Host One', spinnerRank: null, selectedClubId: null, selectedClubName: null, memberUserIds: [HOST.id] },
      { id: 't2', name: 'Guest One', spinnerRank: null, selectedClubId: null, selectedClubName: null, memberUserIds: [GUEST.id] },
    ],
    participants: [
      participant({ userId: HOST.id, displayName: 'Host One', isHost: true, isReady: true }),
      participant({ userId: GUEST.id, displayName: 'Guest One', isReady: false }),
    ],
    requirements: { allAssigned: true, teamsValid: true, ...over },
  })

  it('lets a participant ready up', async () => {
    getMock.mockResolvedValue(readyDetail())
    setReadyMock.mockResolvedValue(readyDetail())
    renderLobby(GUEST)

    const ready = await screen.findByRole('button', { name: /i'm ready/i })
    await userEvent.click(ready)

    await waitFor(() => expect(setReadyMock).toHaveBeenCalledWith('d1', true, 3))
  })

  it('keeps Start disabled until everyone is ready, then enables it', async () => {
    getMock.mockResolvedValue(readyDetail({ allReady: false, canStart: false }))
    const { unmount } = renderLobby(HOST)
    expect(await screen.findByRole('button', { name: /start draft/i })).toBeDisabled()
    unmount()

    getMock.mockResolvedValue(readyDetail({ allReady: true, canStart: true }))
    renderLobby(HOST)
    expect(await screen.findByRole('button', { name: /start draft/i })).toBeEnabled()
  })
})

describe('LobbyPage — spinner', () => {
  const spinnerDetail = (teams: DraftTeam[]) => detail({
    status: 'SpinnerRanking',
    teams,
    participants: [
      participant({ userId: HOST.id, displayName: 'Host One', isHost: true, isReady: true }),
      participant({ userId: GUEST.id, displayName: 'Guest One', isReady: true }),
    ],
  })

  const uncommitted: DraftTeam[] = [
    { id: 't1', name: 'Host One', spinnerRank: null, selectedClubId: null, selectedClubName: null, memberUserIds: [HOST.id] },
    { id: 't2', name: 'Guest One', spinnerRank: null, selectedClubId: null, selectedClubName: null, memberUserIds: [GUEST.id] },
  ]

  it('lets the host spin the wheel', async () => {
    getMock.mockResolvedValue(spinnerDetail(uncommitted))
    commitSpinnerMock.mockResolvedValue(spinnerDetail(uncommitted))
    renderLobby(HOST)

    const spin = await screen.findByRole('button', { name: /spin the wheel/i })
    await userEvent.click(spin)

    await waitFor(() => expect(commitSpinnerMock).toHaveBeenCalledWith('d1', 3))
  })

  it('reveals the committed order (reduced-motion equivalent)', async () => {
    // Force reduced motion so the order list reveals immediately without timers.
    window.matchMedia = ((query: string) => ({
      matches: query.includes('reduce'), media: query, onchange: null,
      addEventListener: () => {}, removeEventListener: () => {}, addListener: () => {}, removeListener: () => {}, dispatchEvent: () => false,
    })) as unknown as typeof window.matchMedia

    getMock.mockResolvedValue(spinnerDetail([
      { id: 't1', name: 'Host One', spinnerRank: 2, selectedClubId: null, selectedClubName: null, memberUserIds: [HOST.id] },
      { id: 't2', name: 'Guest One', spinnerRank: 1, selectedClubId: null, selectedClubName: null, memberUserIds: [GUEST.id] },
    ]))
    renderLobby(GUEST)

    const order = await screen.findByRole('list', { name: /committed spinner order/i })
    const rows = within(order).getAllByRole('listitem')
    // Guest One ranks first (rank 1), Host One second (rank 2) — order comes from the server, not the DOM.
    expect(rows[0]).toHaveTextContent('Guest One')
    expect(rows[1]).toHaveTextContent('Host One')
  })

  it('lets the host open club selection once the order is committed', async () => {
    getMock.mockResolvedValue(spinnerDetail([
      { id: 't1', name: 'Host One', spinnerRank: 1, selectedClubId: null, selectedClubName: null, memberUserIds: [HOST.id] },
      { id: 't2', name: 'Guest One', spinnerRank: 2, selectedClubId: null, selectedClubName: null, memberUserIds: [GUEST.id] },
    ]))
    ;(draftsApi.openClubSelection as unknown as Mock).mockResolvedValue(detail({ status: 'ClubSelection' }))
    renderLobby(HOST)

    const open = await screen.findByRole('button', { name: /open club selection/i })
    await userEvent.click(open)
    await waitFor(() => expect(draftsApi.openClubSelection).toHaveBeenCalledWith('d1', 3))
  })
})

describe('LobbyPage — club selection', () => {
  const teams: DraftTeam[] = [
    { id: 't1', name: 'Host One', spinnerRank: 1, selectedClubId: null, selectedClubName: null, memberUserIds: [HOST.id] },
    { id: 't2', name: 'Guest One', spinnerRank: 2, selectedClubId: null, selectedClubName: null, memberUserIds: [GUEST.id] },
  ]
  const clubDetail = () => detail({
    status: 'ClubSelection',
    teams,
    participants: [
      participant({ userId: HOST.id, displayName: 'Host One', isHost: true, isReady: true }),
      participant({ userId: GUEST.id, displayName: 'Guest One', isReady: true }),
    ],
    turn: {
      phase: 'ClubSelection', direction: 'Straight', round: 0,
      activeTeamId: 't1', activeTeamName: 'Host One', activeTeamMemberUserIds: [HOST.id],
      activeSlotOrder: 0, activeSlotLabel: 'Held player',
    },
  })

  it('lets the active team choose a club then protect a player', async () => {
    getMock.mockResolvedValue(clubDetail())
    boardMock.mockImplementation((_id: string, clubId?: string) => Promise.resolve(clubId
      ? board({ isMyTurn: true, eligibleFootballers: [{ id: 501, name: 'Vinicius Jr', overall: 89, clubId: 'c1', clubName: 'Real Madrid', positions: ['LW'] }] })
      : board({ isMyTurn: true, availableClubs: [{ id: 'c1', name: 'Real Madrid', league: 'LALIGA' }] })))
    selectClubMock.mockResolvedValue(clubDetail())
    renderLobby(HOST)

    const select = await screen.findByLabelText(/five-star club/i)
    await userEvent.selectOptions(select, 'c1')

    const protect = await screen.findByRole('button', { name: /protect/i })
    await userEvent.click(protect)
    await waitFor(() => expect(selectClubMock).toHaveBeenCalledWith('d1', 'c1', 501, 3))
  })

  it('shows a waiting message when it is not your turn', async () => {
    getMock.mockResolvedValue(clubDetail())
    boardMock.mockResolvedValue(board({ isMyTurn: false, availableClubs: [] }))
    renderLobby(GUEST) // Guest is rank 2 — not on the clock

    expect(await screen.findByText(/waiting for host one to choose/i)).toBeInTheDocument()
  })
})

describe('LobbyPage — position draft', () => {
  const teams: DraftTeam[] = [
    { id: 't1', name: 'Host One', spinnerRank: 1, selectedClubId: 'c1', selectedClubName: 'Real Madrid', memberUserIds: [HOST.id] },
    { id: 't2', name: 'Guest One', spinnerRank: 2, selectedClubId: 'c2', selectedClubName: 'Arsenal', memberUserIds: [GUEST.id] },
  ]
  const positionDetail = () => detail({
    status: 'PositionDraft',
    teams,
    participants: [
      participant({ userId: HOST.id, displayName: 'Host One', isHost: true, isReady: true }),
      participant({ userId: GUEST.id, displayName: 'Guest One', isReady: true }),
    ],
    turn: {
      phase: 'PositionDraft', direction: 'Ascending', round: 1,
      activeTeamId: 't1', activeTeamName: 'Host One', activeTeamMemberUserIds: [HOST.id],
      activeSlotOrder: 1, activeSlotLabel: 'ST', activeSlotPosition: 'ST', slotAcceptsAnyPosition: false,
    },
  })

  it('lets the active team draft an eligible player for the open slot', async () => {
    getMock.mockResolvedValue(positionDetail())
    boardMock.mockResolvedValue(board({
      status: 'PositionDraft', isMyTurn: true,
      eligibleFootballers: [{ id: 700, name: 'Harry Kane', overall: 90, clubId: 'c3', clubName: 'Bayern', positions: ['ST'] }],
    }))
    submitPickMock.mockResolvedValue(positionDetail())
    renderLobby(HOST)

    const draft = await screen.findByRole('button', { name: /draft/i })
    await userEvent.click(draft)
    await waitFor(() => expect(submitPickMock).toHaveBeenCalledWith('d1', 700, 3))
  })
})

describe('LobbyPage — completed', () => {
  it('shows the final squads', async () => {
    getMock.mockResolvedValue(detail({
      status: 'Completed',
      teams: [{ id: 't1', name: 'Host One', spinnerRank: 1, selectedClubId: 'c1', selectedClubName: 'Real Madrid', memberUserIds: [HOST.id] }],
      slots: [{ order: 0, slotType: 'Held', position: null, label: 'Held player' }, { order: 1, slotType: 'StartingPosition', position: 'ST', label: 'ST' }],
      picks: [
        { teamId: 't1', slotOrder: 0, footballerId: 1, footballerName: 'Jude Bellingham', footballerOverall: 90, footballerPosition: 'CM', pickedByParticipantId: null },
        { teamId: 't1', slotOrder: 1, footballerId: 2, footballerName: 'Kylian Mbappe', footballerOverall: 91, footballerPosition: 'ST', pickedByParticipantId: null },
      ],
    }))
    renderLobby(HOST)

    expect(await screen.findByText(/filled all 16 squad slots/i)).toBeInTheDocument()
    expect(screen.getByText(/Kylian Mbappe/)).toBeInTheDocument()
  })
})
