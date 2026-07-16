import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { DraftRoomStage } from './DraftRoomStage'
import { draftsApi } from '../../services/api'
import { GUEST, HOST, board, detail } from '../../test/draftFactories'
import type { DraftDetail, DraftFootballerCard, DraftTeam } from '../../types/draft'

vi.mock('../../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../services/api')>()
  return {
    ...actual,
    draftsApi: {
      ...actual.draftsApi,
      board: vi.fn(),
      submitPick: vi.fn(),
      footballerCard: vi.fn(),
    },
  }
})

const boardMock = draftsApi.board as unknown as Mock
const submitPickMock = draftsApi.submitPick as unknown as Mock
const cardMock = draftsApi.footballerCard as unknown as Mock

const teams: DraftTeam[] = [
  { id: 't1', name: 'Host One', spinnerRank: 1, selectedClubId: 'c1', selectedClubName: 'Real Madrid', memberUserIds: [HOST.id] },
  { id: 't2', name: 'Guest One', spinnerRank: 2, selectedClubId: 'c2', selectedClubName: 'Arsenal', memberUserIds: [GUEST.id] },
]

const slots = [
  { order: 0, slotType: 'Held', position: null, label: 'Held player' },
  { order: 1, slotType: 'StartingPosition', position: 'ST', label: 'ST' },
  { order: 2, slotType: 'StartingPosition', position: 'LW', label: 'LW' },
]

const KANE = { id: 700, name: 'Harry Kane', overall: 90, clubId: 'c3', clubName: 'Bayern', positions: ['ST'] }

function roomDetail(over?: Parameters<typeof detail>[0]): DraftDetail {
  return detail({
    status: 'PositionDraft',
    teams,
    slots,
    turn: {
      phase: 'PositionDraft', direction: 'Ascending', round: 1,
      activeTeamId: 't1', activeTeamName: 'Host One', activeTeamMemberUserIds: [HOST.id],
      activeSlotOrder: 1, activeSlotLabel: 'ST', activeSlotPosition: 'ST', slotAcceptsAnyPosition: false,
    },
    ...over,
  })
}

function kaneCard(over?: Partial<DraftFootballerCard>): DraftFootballerCard {
  return {
    card: {
      id: 700, name: 'Harry Kane', fullName: 'Harry Edward Kane', overall: 90,
      clubId: 'c3', clubName: 'Bayern', league: 'Bundesliga', nation: 'England',
      positions: ['ST', 'CF'],
      stats: [{ label: 'PAC', value: 70 }, { label: 'SHO', value: 93 }],
      roles: [{ position: 'ST', name: 'Advanced Forward', familiarity: 2 }],
      playStyles: [{ name: 'Power Shot', plus: true }, { name: 'Dead Ball', plus: false }],
      imageUrl: null,
    },
    isTaken: false, takenByTeamId: null, takenByTeamName: null, takenSlotLabel: null,
    ...over,
  }
}

/** Mirrors LobbyPage.mutate: run the action with the current version; failures are swallowed there. */
const mutate = vi.fn(async (action: (version: number) => Promise<DraftDetail>) => { await action(3) })

function renderRoom(props?: { detail?: DraftDetail; userId?: string }) {
  return render(
    <DraftRoomStage
      detail={props?.detail ?? roomDetail()}
      busy={false}
      userId={props?.userId ?? HOST.id}
      hubStatus="connected"
      mutate={mutate as never}
    />,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
  boardMock.mockResolvedValue(board({ status: 'PositionDraft', isMyTurn: true, eligibleFootballers: [KANE] }))
  submitPickMock.mockResolvedValue(roomDetail())
  cardMock.mockResolvedValue(kaneCard())
})

describe('DraftRoomStage — header and chrome', () => {
  it('shows the active team, slot, progress, connection state, and an explicit your-turn state', async () => {
    renderRoom()

    expect(await screen.findByText('Host One · ST')).toBeInTheDocument()
    expect(screen.getByText(/needs a st/i)).toBeInTheDocument()
    expect(screen.getByText(/pick 1 of 6/i)).toBeInTheDocument() // 2 teams × 3 slots
    expect(screen.getByText('Your turn')).toBeInTheDocument() // explicit critical state, no animation needed
    expect(screen.getByRole('status', { name: /live connection/i })).toHaveTextContent('Live')
  })

  it('replaces the global bottom navigation with the sticky draft action area (§8.3)', () => {
    const { unmount } = renderRoom()

    expect(document.body.classList.contains('draft-room-live')).toBe(true)
    expect(screen.getByRole('region', { name: /draft room actions/i })).toBeInTheDocument()

    unmount()
    expect(document.body.classList.contains('draft-room-live')).toBe(false)
  })
})

describe('DraftRoomStage — search and paging', () => {
  it('searches the eligible pool on the server, inside the pinned board', async () => {
    renderRoom()
    await screen.findByText('Harry Kane')

    await userEvent.type(screen.getByLabelText(/search eligible players/i), 'kane')

    await waitFor(() => expect(boardMock).toHaveBeenCalledWith('d1', { search: 'kane' }))
  })

  it('raises the pool size deliberately instead of fetching everything', async () => {
    const bigPool = Array.from({ length: 100 }, (_, index) => ({
      id: 1000 + index, name: `Striker ${index}`, overall: 90 - (index % 10), clubId: 'c9', clubName: 'Club', positions: ['ST'],
    }))
    boardMock.mockResolvedValue(board({ status: 'PositionDraft', isMyTurn: true, eligibleFootballers: bigPool }))
    renderRoom()

    await userEvent.click(await screen.findByRole('button', { name: /show more players/i }))

    await waitFor(() => expect(boardMock).toHaveBeenCalledWith('d1', { take: 300 }))
  })

  it('hides draft actions and explains the wait when it is not your turn', async () => {
    boardMock.mockResolvedValue(board({ status: 'PositionDraft', isMyTurn: false, eligibleFootballers: [KANE] }))
    renderRoom({ userId: GUEST.id })

    expect(await screen.findByText('Harry Kane')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Draft Harry Kane' })).not.toBeInTheDocument()
    expect(screen.getByText(/waiting for host one to pick/i)).toBeInTheDocument()
  })
})

describe('DraftRoomStage — player detail card (§9.6)', () => {
  it('shows stats, roles with ++, PlayStyles, and club/league/nation on demand', async () => {
    renderRoom()

    await userEvent.click(await screen.findByRole('button', { name: 'View Harry Kane details' }))

    const sheet = await screen.findByRole('dialog', { name: 'Harry Kane' })
    expect(cardMock).toHaveBeenCalledWith('d1', 700)
    expect(within(sheet).getByText('Bayern · Bundesliga · England')).toBeInTheDocument()
    expect(within(sheet).getByText('93')).toBeInTheDocument() // SHO card stat
    expect(within(sheet).getByText('Advanced Forward')).toBeInTheDocument()
    expect(within(sheet).getByText('++')).toBeInTheDocument() // role familiarity
    expect(within(sheet).getByText(/power shot/i)).toBeInTheDocument()
    // Drafting from the card still goes through the confirmation sheet.
    await userEvent.click(within(sheet).getByRole('button', { name: /draft harry kane/i }))
    expect(await screen.findByRole('dialog', { name: /confirm draft/i })).toBeInTheDocument()
  })

  it('explains who holds an unavailable player instead of offering a pick', async () => {
    cardMock.mockResolvedValue(kaneCard({ isTaken: true, takenByTeamId: 't2', takenByTeamName: 'Guest One', takenSlotLabel: 'ST' }))
    renderRoom()

    await userEvent.click(await screen.findByRole('button', { name: 'View Harry Kane details' }))

    const sheet = await screen.findByRole('dialog', { name: 'Harry Kane' })
    expect(within(sheet).getByText(/unavailable — already drafted/i)).toBeInTheDocument()
    expect(within(sheet).getByText(/guest one holds harry kane \(st\)/i)).toBeInTheDocument()
    expect(within(sheet).queryByRole('button', { name: /draft harry kane/i })).not.toBeInTheDocument()
  })
})

describe('DraftRoomStage — pick confirmation', () => {
  it('never picks in one tap: the sheet names the team and slot, and cancel makes no call', async () => {
    renderRoom()

    await userEvent.click(await screen.findByRole('button', { name: 'Draft Harry Kane' }))
    const sheet = await screen.findByRole('dialog', { name: /confirm draft/i })
    expect(sheet).toHaveTextContent('Host One')
    expect(sheet).toHaveTextContent('ST')

    await userEvent.click(within(sheet).getByRole('button', { name: /^cancel$/i }))
    expect(submitPickMock).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: 'Draft Harry Kane' }))
    await userEvent.click(within(await screen.findByRole('dialog', { name: /confirm draft/i })).getByRole('button', { name: /confirm draft/i }))
    await waitFor(() => expect(submitPickMock).toHaveBeenCalledWith('d1', 700, 3))
  })
})

describe('DraftRoomStage — personal shortlist (decision 11)', () => {
  it('stars persist per user and draft, and the shortlist view marks taken players', async () => {
    renderRoom()

    await userEvent.click(await screen.findByRole('button', { name: 'Add Harry Kane to shortlist' }))
    expect(JSON.parse(localStorage.getItem(`fc-draft-shortlist:${HOST.id}:d1`) ?? '[]')).toHaveLength(1)

    await userEvent.click(screen.getByRole('button', { name: /shortlist \(1\)/i }))
    const list = screen.getByRole('list', { name: /shortlisted players/i })
    expect(within(list).getByText('Harry Kane')).toBeInTheDocument()
    expect(within(list).getByRole('button', { name: 'Draft Harry Kane' })).toBeInTheDocument()
  })

  it('marks a shortlisted player who has since been taken and disables drafting them', async () => {
    localStorage.setItem(
      `fc-draft-shortlist:${HOST.id}:d1`,
      JSON.stringify([{ id: 900, name: 'Taken Star', overall: 88, positions: ['ST'], clubName: 'Club' }]),
    )
    const withPick = roomDetail({
      picks: [{ teamId: 't2', slotOrder: 0, footballerId: 900, footballerName: 'Taken Star', footballerOverall: 88, footballerPosition: 'ST', pickedByParticipantId: null }],
    })
    renderRoom({ detail: withPick })

    await userEvent.click(await screen.findByRole('button', { name: /shortlist \(1\)/i }))
    const list = screen.getByRole('list', { name: /shortlisted players/i })
    expect(within(list).getByText(/· Taken/)).toBeInTheDocument()
    expect(within(list).queryByRole('button', { name: 'Draft Taken Star' })).not.toBeInTheDocument()
  })
})
