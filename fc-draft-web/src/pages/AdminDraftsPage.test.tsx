import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { AxiosError, AxiosHeaders } from 'axios'
import { AdminDraftsPage } from './AdminDraftsPage'
import { draftsApi } from '../services/api'
import type { DraftDetail } from '../types/draft'
import { detail } from '../test/draftFactories'

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return {
    ...actual,
    draftsApi: {
      ...actual.draftsApi,
      list: vi.fn(), get: vi.fn(), pause: vi.fn(), resume: vi.fn(), cancel: vi.fn(),
    },
  }
})

const listMock = draftsApi.list as unknown as Mock
const getMock = draftsApi.get as unknown as Mock
const pauseMock = draftsApi.pause as unknown as Mock
const resumeMock = draftsApi.resume as unknown as Mock
const cancelMock = draftsApi.cancel as unknown as Mock

const EVENTS = [
  { sequence: 1, type: 'DraftCreated', fromStatus: null, toStatus: 'Lobby', version: 1, actorUserId: 'host-1', reason: null, createdAt: '2026-07-15T00:00:00Z' },
  { sequence: 2, type: 'ParticipantJoined', fromStatus: 'Lobby', toStatus: 'Lobby', version: 2, actorUserId: 'guest-1', reason: null, createdAt: '2026-07-15T00:01:00Z' },
]

function live(over?: Parameters<typeof detail>[0]): DraftDetail {
  return detail({ status: 'PositionDraft', events: EVENTS, version: 7, ...over })
}

function conflict(): AxiosError {
  const headers = new AxiosHeaders()
  return new AxiosError('Conflict', 'ERR_BAD_REQUEST', { headers }, {}, {
    status: 409, statusText: 'Conflict', headers, config: { headers },
    data: { detail: 'The draft version is stale.' },
  })
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/admin/drafts']}>
      <AdminDraftsPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  listMock.mockResolvedValue([live().summary])
  getMock.mockResolvedValue(live())
})

describe('AdminDraftsPage (PR-21 draft operations)', () => {
  it('inspects a draft: state, version, participants, and the append-only event history', async () => {
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: /inspect tuesday draft/i }))

    const dialog = await screen.findByRole('dialog', { name: /tuesday draft/i })
    expect(within(dialog).getByText(/ABC123 · v7/)).toBeInTheDocument()

    // Participant names also appear as event actors — scope to the participants list.
    const participants = within(dialog).getByRole('heading', { name: /participants/i }).closest('section')!
    expect(within(participants).getByText('Host One')).toBeInTheDocument()
    expect(within(participants).getByText('Guest One')).toBeInTheDocument()

    expect(within(dialog).getByRole('heading', { name: /event history \(2\)/i })).toBeInTheDocument()
    expect(within(dialog).getByRole('cell', { name: 'DraftCreated' })).toBeInTheDocument()
    expect(within(dialog).getByRole('cell', { name: 'ParticipantJoined' })).toBeInTheDocument()
  })

  it('pauses a live draft only with a reason, version-checked', async () => {
    pauseMock.mockResolvedValue(live({ status: 'Paused', version: 8 }))
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: /inspect tuesday draft/i }))
    const dialog = await screen.findByRole('dialog', { name: /tuesday draft/i })
    await userEvent.click(within(dialog).getByRole('button', { name: /pause…/i }))

    // The confirm stays locked until a reason is captured.
    const confirm = within(dialog).getByRole('button', { name: /confirm pause/i })
    expect(confirm).toBeDisabled()
    await userEvent.type(within(dialog).getByLabelText(/reason/i), 'Connection dispute')
    expect(confirm).toBeEnabled()
    await userEvent.click(confirm)

    await waitFor(() => expect(pauseMock).toHaveBeenCalledWith('d1', 'Connection dispute', 7))
    expect(await screen.findByText(/was paused/i)).toBeInTheDocument()
  })

  it('offers resume for a paused draft and gates cancel behind a destructive confirmation with a reason', async () => {
    getMock.mockResolvedValue(live({ status: 'Paused' }))
    cancelMock.mockResolvedValue(live({ status: 'Cancelled', version: 9 }))
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: /inspect tuesday draft/i }))
    const dialog = await screen.findByRole('dialog', { name: /tuesday draft/i })

    // Status-aware operations: a Paused draft can resume or cancel, never pause again.
    expect(within(dialog).getByRole('button', { name: /pause…/i })).toBeDisabled()
    expect(within(dialog).getByRole('button', { name: /resume…/i })).toBeEnabled()

    await userEvent.click(within(dialog).getByRole('button', { name: /cancel draft…/i }))
    const confirm = within(dialog).getByRole('button', { name: /confirm cancellation/i })
    expect(confirm).toBeDisabled()
    await userEvent.type(within(dialog).getByLabelText(/reason/i), 'Rules dispute')
    await userEvent.click(confirm)

    await waitFor(() => expect(cancelMock).toHaveBeenCalledWith('d1', 'Rules dispute', 7))
  })

  it('never offers operations on a completed draft', async () => {
    getMock.mockResolvedValue(live({ status: 'Completed' }))
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: /inspect tuesday draft/i }))
    const dialog = await screen.findByRole('dialog', { name: /tuesday draft/i })
    expect(within(dialog).getByRole('button', { name: /pause…/i })).toBeDisabled()
    expect(within(dialog).getByRole('button', { name: /resume…/i })).toBeDisabled()
    expect(within(dialog).getByRole('button', { name: /cancel draft…/i })).toBeDisabled()
    expect(within(dialog).getByText(/this draft is completed/i)).toBeInTheDocument()
  })

  it('explains a stale-version 409 and resyncs the snapshot', async () => {
    pauseMock.mockRejectedValue(conflict())
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: /inspect tuesday draft/i }))
    const dialog = await screen.findByRole('dialog', { name: /tuesday draft/i })
    await userEvent.click(within(dialog).getByRole('button', { name: /pause…/i }))
    await userEvent.type(within(dialog).getByLabelText(/reason/i), 'Too slow')
    await userEvent.click(within(dialog).getByRole('button', { name: /confirm pause/i }))

    expect(await screen.findByText(/the draft moved on before this action landed/i)).toBeInTheDocument()
    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(2)) // inspect + resync
  })
})
