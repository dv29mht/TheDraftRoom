import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { LobbyPage } from './LobbyPage'
import { draftsApi } from '../services/api'
import { useAuthStore } from '../stores/authStore'
import type { AuthUser } from '../types/auth'
import type { DraftDetail } from '../types/draft'

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
    },
  }
})

const getMock = draftsApi.get as unknown as Mock
const joinMock = draftsApi.join as unknown as Mock
const invitableMock = draftsApi.invitableUsers as unknown as Mock

const HOST: AuthUser = { id: 'host-1', displayName: 'Host One', email: 'host@draftroom.dev', role: 'player' }
const GUEST: AuthUser = { id: 'guest-1', displayName: 'Guest One', email: 'guest@draftroom.dev', role: 'player' }

function lobby(partial?: Partial<DraftDetail['capacity']>): DraftDetail {
  return {
    summary: {
      id: 'd1', code: 'ABC123', name: 'Tuesday Draft', format: '1v1', status: 'Lobby',
      hostUserId: HOST.id, version: 3, pickTimerSeconds: 120, pinnedDatasetVersionId: null,
      participantCount: 2, createdAt: '2026-07-15T00:00:00Z', startedAt: null, completedAt: null,
    },
    capacity: {
      min: 2, max: 10, requiresEven: false, participantCount: 2, joinedCount: 1, invitedCount: 1,
      meetsMinimum: true, withinMaximum: true, meetsEven: true, canLock: true, ...partial,
    },
    participants: [
      { userId: HOST.id, displayName: 'Host One', email: 'host@draftroom.dev', isHost: true, seed: null, status: 'Joined', isReady: false },
      { userId: GUEST.id, displayName: 'Guest One', email: 'guest@draftroom.dev', isHost: false, seed: null, status: 'Invited', isReady: false },
    ],
    teams: [], slots: [], events: [],
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

beforeEach(() => {
  getMock.mockReset()
  joinMock.mockReset()
  invitableMock.mockReset()
  invitableMock.mockResolvedValue([])
})

describe('LobbyPage', () => {
  it('shows the roster and an enabled lock control for the host when capacity is met', async () => {
    getMock.mockResolvedValue(lobby())
    renderLobby(HOST)

    expect(await screen.findByText('Host One')).toBeInTheDocument()
    expect(screen.getByText('Guest One')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /lock lobby/i })).toBeEnabled()
    // The Start-draft control stays disabled and labelled coming soon (PR-12/PR-13).
    expect(screen.getByRole('button', { name: /start draft/i })).toBeDisabled()
  })

  it('disables the lock control below the minimum', async () => {
    getMock.mockResolvedValue(lobby({ canLock: false, meetsMinimum: false, participantCount: 1 }))
    renderLobby(HOST)

    await screen.findByText('Host One')
    expect(screen.getByRole('button', { name: /lock lobby/i })).toBeDisabled()
  })

  it('lets an invited participant confirm presence', async () => {
    getMock.mockResolvedValue(lobby())
    joinMock.mockResolvedValue(lobby({ joinedCount: 2 }))
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
