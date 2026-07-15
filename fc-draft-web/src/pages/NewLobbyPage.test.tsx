import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { NewLobbyPage } from './NewLobbyPage'
import { draftsApi } from '../services/api'

const { navigate } = vi.hoisted(() => ({ navigate: vi.fn() }))

vi.mock('react-router-dom', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router-dom')>()),
  useNavigate: () => navigate,
}))

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return {
    ...actual,
    draftsApi: { ...actual.draftsApi, rosterTemplates: vi.fn(), invitableUsers: vi.fn(), create: vi.fn() },
  }
})

const templatesMock = draftsApi.rosterTemplates as unknown as Mock
const usersMock = draftsApi.invitableUsers as unknown as Mock
const createMock = draftsApi.create as unknown as Mock

beforeEach(() => {
  navigate.mockReset()
  templatesMock.mockReset()
  usersMock.mockReset()
  createMock.mockReset()
  templatesMock.mockResolvedValue([
    { id: 't1', name: '4-3-3 Classic', isActive: true, pickTimerSeconds: 120, slotCount: 16, createdAt: '2026-07-15T00:00:00Z' },
  ])
  usersMock.mockResolvedValue([{ id: 'u2', displayName: 'Sam Player', email: 'sam@draftroom.dev' }])
})

const renderPage = () => render(<MemoryRouter><NewLobbyPage /></MemoryRouter>)

describe('NewLobbyPage', () => {
  it('creates a lobby with the chosen format, template and invites, then routes to it', async () => {
    createMock.mockResolvedValue({ summary: { id: 'd1' } })
    renderPage()

    // The invitee loaded from the directory can be added to the invite list.
    await userEvent.click(await screen.findByRole('button', { name: /invite/i }))
    await userEvent.click(screen.getByRole('button', { name: /create lobby/i }))

    await waitFor(() => expect(createMock).toHaveBeenCalledTimes(1))
    const payload = createMock.mock.calls[0][0]
    expect(payload.format).toBe('2v2')
    expect(payload.rosterTemplateId).toBe('t1')
    expect(payload.inviteUserIds).toEqual(['u2'])
    expect(navigate).toHaveBeenCalledWith('/drafts/d1')
  })

  it('reflects the 1v1 capacity when the format is switched', async () => {
    renderPage()
    await screen.findByText('Sam Player')

    await userEvent.click(screen.getByRole('button', { name: /1v1/i }))

    expect(screen.getByText('10')).toBeInTheDocument() // 1v1 capacity
  })
})
