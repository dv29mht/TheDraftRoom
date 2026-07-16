import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { ResultsPage } from './ResultsPage'
import { draftsApi } from '../services/api'
import { GUEST, HOST } from '../test/draftFactories'
import { useAuthStore } from '../stores/authStore'
import type { AuthUser } from '../types/auth'
import type { DraftResults, ResultPick } from '../types/draft'

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return { ...actual, draftsApi: { ...actual.draftsApi, results: vi.fn() } }
})

const resultsMock = draftsApi.results as unknown as Mock

const slots = [
  { order: 0, slotType: 'Held', position: null, label: 'Held player' },
  { order: 1, slotType: 'StartingPosition', position: 'ST', label: 'ST' },
  { order: 2, slotType: 'StartingPosition', position: 'GK', label: 'GK' },
  { order: 3, slotType: 'FlexBench', position: null, label: 'SUB1' },
]

function resultPick(over: Partial<ResultPick> & { sequence: number; teamId: string; slotOrder: number; footballerName: string }): ResultPick {
  return {
    slotLabel: slots.find((slot) => slot.order === over.slotOrder)?.label ?? 'Slot',
    slotPosition: slots.find((slot) => slot.order === over.slotOrder)?.position ?? null,
    footballerId: over.sequence * 11,
    footballerOverall: 88,
    footballerPosition: null,
    clubName: 'Real Madrid',
    league: 'LALIGA',
    nation: 'Spain',
    ...over,
  }
}

function results(): DraftResults {
  return {
    summary: {
      id: 'd1', code: 'ABC123', name: 'Tuesday Draft', format: '1v1', status: 'Completed',
      hostUserId: HOST.id, version: 40, pickTimerSeconds: 120, pinnedDatasetVersionId: 'v1',
      participantCount: 2, createdAt: '2026-07-15T00:00:00Z', startedAt: '2026-07-15T01:00:00Z', completedAt: '2026-07-15T02:00:00Z',
    },
    slots,
    teams: [
      {
        teamId: 't1', name: 'Host One', spinnerRank: 1, selectedClubName: 'Real Madrid',
        memberUserIds: [HOST.id], memberNames: ['Host One'], averageOverall: 88.5,
        lineRatings: [
          { line: 'GK', average: 84, filled: 1, slotCount: 1 },
          { line: 'DEF', average: null, filled: 0, slotCount: 0 },
          { line: 'MID', average: null, filled: 0, slotCount: 0 },
          { line: 'FWD', average: 91, filled: 1, slotCount: 1 },
        ],
        clubs: ['Real Madrid', 'Bayern'], leagues: ['LALIGA', 'Bundesliga'], nations: ['Spain', 'England'],
        picks: [
          resultPick({ sequence: 1, teamId: 't1', slotOrder: 0, footballerName: 'Vini Jr', footballerOverall: 89 }),
          resultPick({ sequence: 3, teamId: 't1', slotOrder: 1, footballerName: 'Harry Kane', footballerOverall: 91, clubName: 'Bayern', league: 'Bundesliga', nation: 'England' }),
          resultPick({ sequence: 5, teamId: 't1', slotOrder: 2, footballerName: 'Neuer', footballerOverall: 84, clubName: 'Bayern', league: 'Bundesliga', nation: 'Germany' }),
        ],
      },
      {
        teamId: 't2', name: 'Guest One', spinnerRank: 2, selectedClubName: 'Arsenal',
        memberUserIds: [GUEST.id], memberNames: ['Guest One'], averageOverall: 86,
        lineRatings: [
          { line: 'GK', average: 82, filled: 1, slotCount: 1 },
          { line: 'DEF', average: null, filled: 0, slotCount: 0 },
          { line: 'MID', average: null, filled: 0, slotCount: 0 },
          { line: 'FWD', average: 90, filled: 1, slotCount: 1 },
        ],
        clubs: ['Arsenal'], leagues: ['Premier League'], nations: ['Norway'],
        picks: [
          resultPick({ sequence: 2, teamId: 't2', slotOrder: 0, footballerName: 'Saka', footballerOverall: 87 }),
          resultPick({ sequence: 4, teamId: 't2', slotOrder: 1, footballerName: 'Haaland', footballerOverall: 90 }),
        ],
      },
    ],
    pickSequence: [
      resultPick({ sequence: 1, teamId: 't1', slotOrder: 0, footballerName: 'Vini Jr' }),
      resultPick({ sequence: 2, teamId: 't2', slotOrder: 0, footballerName: 'Saka' }),
      resultPick({ sequence: 3, teamId: 't1', slotOrder: 1, footballerName: 'Harry Kane' }),
      resultPick({ sequence: 4, teamId: 't2', slotOrder: 1, footballerName: 'Haaland' }),
      resultPick({ sequence: 5, teamId: 't1', slotOrder: 2, footballerName: 'Neuer' }),
    ],
  }
}

function renderResults(user: AuthUser = GUEST) {
  useAuthStore.setState({ user, accessToken: 't', mustChangePassword: false })
  return render(
    <MemoryRouter initialEntries={['/drafts/d1/results']}>
      <Routes><Route path="/drafts/:draftId/results" element={<ResultsPage />} /></Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  resultsMock.mockResolvedValue(results())
})

describe('ResultsPage', () => {
  it('focuses my squad first, shows ratings, and renders the formation from the frozen slots', async () => {
    renderResults(GUEST) // the viewer is Guest One — their squad opens first

    expect(await screen.findByRole('heading', { name: 'Tuesday Draft' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /guest one · you/i })).toHaveClass('is-focused')

    // Ratings chips: squad average + the four lines.
    const ratings = screen.getByLabelText('Squad ratings')
    expect(within(ratings).getByText('86')).toBeInTheDocument()
    expect(within(ratings).getByText('GK')).toBeInTheDocument()

    // Formation view places the XI on the pitch; held/bench stay listed below.
    const pitch = screen.getByRole('img', { name: /guest one starting eleven/i })
    expect(within(pitch).getByText('Haaland')).toBeInTheDocument()
    expect(within(screen.getByLabelText(/held player and bench/i)).getByText(/saka/i)).toBeInTheDocument()
  })

  it('switches team and view, showing the list with club, league, and nation', async () => {
    renderResults(GUEST)
    await screen.findByRole('heading', { name: 'Tuesday Draft' })

    await userEvent.click(screen.getByRole('button', { name: /host one/i }))
    await userEvent.click(screen.getByRole('button', { name: /^list$/i }))

    const list = screen.getByRole('list', { name: /host one squad list/i })
    expect(within(list).getByText(/harry kane · 91/i)).toBeInTheDocument()
    expect(within(list).getByText('Real Madrid · LALIGA · Spain')).toBeInTheDocument()

    // Represented summaries come from the pinned dataset extras.
    expect(screen.getByText('Bundesliga')).toBeInTheDocument()
  })

  it('renders the full pick sequence in acceptance order', async () => {
    renderResults(HOST)
    await screen.findByRole('heading', { name: 'Tuesday Draft' })

    const sequence = screen.getByRole('list', { name: /pick sequence/i })
    const rows = within(sequence).getAllByRole('listitem')
    expect(rows).toHaveLength(5)
    expect(rows[0]).toHaveTextContent('#1')
    expect(rows[0]).toHaveTextContent('Vini Jr')
    expect(rows[4]).toHaveTextContent('Neuer')
  })

  it('shows the archive empty state on 404 (not completed or not visible)', async () => {
    resultsMock.mockRejectedValue({ response: { status: 404 } })
    renderResults(HOST)

    expect(await screen.findByText(/no results here/i)).toBeInTheDocument()
  })
})
