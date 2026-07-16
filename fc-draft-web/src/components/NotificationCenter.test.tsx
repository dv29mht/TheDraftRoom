import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { NotificationCenter } from './NotificationCenter'
import { meApi } from '../services/api'
import type { UserNotification, UserNotifications } from '../types/draft'

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return {
    ...actual,
    meApi: {
      ...actual.meApi,
      notifications: vi.fn(),
      markRead: vi.fn(),
      markAllRead: vi.fn(),
    },
  }
})

const listMock = meApi.notifications as unknown as Mock
const markReadMock = meApi.markRead as unknown as Mock
const markAllMock = meApi.markAllRead as unknown as Mock

function notification(over?: Partial<UserNotification>): UserNotification {
  return {
    id: 'n1', type: 'draft.invited', title: "You're invited: Tuesday Draft",
    body: 'Open the lobby and confirm you are in.', draftId: 'd1',
    readAt: null, createdAt: '2026-07-16T10:00:00Z', ...over,
  }
}

function inbox(items: UserNotification[], unreadCount?: number): UserNotifications {
  return { items, unreadCount: unreadCount ?? items.filter((item) => item.readAt == null).length }
}

function renderCenter() {
  return render(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route path="/" element={<NotificationCenter />} />
        <Route path="/drafts/:draftId" element={<div>Lobby route</div>} />
        <Route path="/drafts/:draftId/results" element={<div>Results route</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  listMock.mockResolvedValue(inbox([notification()]))
})

describe('NotificationCenter (PR-20)', () => {
  it('shows the unread badge and lists persistent notifications', async () => {
    renderCenter()

    const trigger = await screen.findByRole('button', { name: /1 unread notifications/i })
    await userEvent.click(trigger)

    const popover = screen.getByRole('region', { name: /your notifications/i })
    expect(within(popover).getByText(/you're invited: tuesday draft/i)).toBeInTheDocument()
  })

  it('marks a notification read and deep-links to its draft', async () => {
    markReadMock.mockResolvedValue(inbox([notification({ readAt: '2026-07-16T11:00:00Z' })]))
    renderCenter()

    await userEvent.click(await screen.findByRole('button', { name: /1 unread notifications/i }))
    await userEvent.click(screen.getByRole('button', { name: /you're invited: tuesday draft/i }))

    await waitFor(() => expect(markReadMock).toHaveBeenCalledWith('n1'))
    expect(await screen.findByText('Lobby route')).toBeInTheDocument() // deep link followed
  })

  it('routes a completed-draft notification straight to the results', async () => {
    listMock.mockResolvedValue(inbox([notification({ id: 'n2', type: 'draft.completed', title: 'Draft complete: Tuesday Draft' })]))
    markReadMock.mockResolvedValue(inbox([]))
    renderCenter()

    await userEvent.click(await screen.findByRole('button', { name: /1 unread notifications/i }))
    await userEvent.click(screen.getByRole('button', { name: /draft complete: tuesday draft/i }))

    expect(await screen.findByText('Results route')).toBeInTheDocument()
  })

  it('marks everything read in one action', async () => {
    listMock.mockResolvedValue(inbox([notification(), notification({ id: 'n3', title: 'Reminder: Tuesday Draft', type: 'draft.reminder' })]))
    markAllMock.mockResolvedValue(inbox([
      notification({ readAt: '2026-07-16T11:00:00Z' }),
      notification({ id: 'n3', readAt: '2026-07-16T11:00:00Z' }),
    ], 0))
    renderCenter()

    await userEvent.click(await screen.findByRole('button', { name: /2 unread notifications/i }))
    await userEvent.click(screen.getByRole('button', { name: /mark all read/i }))

    await waitFor(() => expect(markAllMock).toHaveBeenCalled())
    expect(screen.getByRole('button', { name: /0 unread notifications/i })).toBeInTheDocument()
  })
})
