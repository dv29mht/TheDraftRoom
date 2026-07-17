import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { AxiosError, AxiosHeaders } from 'axios'
import { AdminCommunicationsPage } from './AdminCommunicationsPage'
import { announcementsApi, draftsApi, emailOutboxApi } from '../services/api'
import type { Announcement, AnnouncementPreviewResponse } from '../types/admin'
import { detail } from '../test/draftFactories'

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return {
    ...actual,
    announcementsApi: { preview: vi.fn(), send: vi.fn(), list: vi.fn() },
    draftsApi: { ...actual.draftsApi, list: vi.fn() },
    emailOutboxApi: { recent: vi.fn() },
  }
})

const previewMock = announcementsApi.preview as unknown as Mock
const sendMock = announcementsApi.send as unknown as Mock
const listMock = announcementsApi.list as unknown as Mock
const draftsMock = draftsApi.list as unknown as Mock
const outboxMock = emailOutboxApi.recent as unknown as Mock

function previewResponse(over?: Partial<AnnouncementPreviewResponse['preview']>): AnnouncementPreviewResponse {
  return {
    preview: {
      subject: 'Season 2 opens', body: 'New dataset live.', audience: 'all', draftId: null, draftName: null,
      audienceLabel: 'All active players', recipientCount: 12, emailRecipientCount: 10, optedOutCount: 2,
      ...over,
    },
    senderName: 'The Draft Room', senderEmail: 'noreply@draftroom.dev', emailConfigured: true,
  }
}

function announcement(over?: Partial<Announcement>): Announcement {
  return {
    id: 'a1', subject: 'Season 2 opens', body: 'New dataset live.', audience: 'all', draftId: null,
    audienceLabel: 'All active players', recipientCount: 12, emailCount: 10, optedOutCount: 2,
    requestedByUserId: 'admin-1', requestedByEmail: 'admin@draftroom.dev', requestedAt: '2026-07-16T12:00:00Z',
    emailsPending: 10, emailsSent: 0, emailsFailed: 0, ...over,
  }
}

function conflict(): AxiosError {
  const headers = new AxiosHeaders()
  return new AxiosError('Conflict', 'ERR_BAD_REQUEST', { headers }, {}, {
    status: 409, statusText: 'Conflict', headers, config: { headers },
    data: { detail: 'The audience changed since the preview.' },
  })
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/admin/communications']}>
      <AdminCommunicationsPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  draftsMock.mockResolvedValue([detail().summary])
  listMock.mockResolvedValue([])
  outboxMock.mockResolvedValue([])
})

describe('AdminCommunicationsPage (PR-21 §9.8)', () => {
  it('requires subject and body before a preview, and keeps the Brevo-template control visibly disabled', async () => {
    renderPage()

    const previewButton = await screen.findByRole('button', { name: /preview announcement/i })
    expect(previewButton).toBeDisabled()

    // §12.4: the deferred templated-announcement control is present and disabled, not absent.
    expect(screen.getByRole('button', { name: /use a brevo template · coming soon/i })).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/subject/i), 'Season 2 opens')
    expect(previewButton).toBeDisabled()
    await userEvent.type(screen.getByLabelText(/message/i), 'New dataset live.')
    expect(previewButton).toBeEnabled()
  })

  it('walks compose → preview → explicit confirmation, sending the previewed recipient count', async () => {
    previewMock.mockResolvedValue(previewResponse())
    sendMock.mockResolvedValue(announcement())
    renderPage()

    await userEvent.type(await screen.findByLabelText(/subject/i), 'Season 2 opens')
    await userEvent.type(screen.getByLabelText(/message/i), 'New dataset live.')
    await userEvent.click(screen.getByRole('button', { name: /preview announcement/i }))

    // The §9.8 preview: sender, audience count, opt-out split, subject, and body — before any send.
    const dialog = await screen.findByRole('dialog', { name: /review before sending/i })
    expect(within(dialog).getByText(/The Draft Room/)).toBeInTheDocument()
    expect(within(dialog).getByText(/All active players/)).toBeInTheDocument()
    expect(within(dialog).getByText(/12 in-app · 10 by email · 2 opted out/)).toBeInTheDocument()
    expect(within(dialog).getByText('New dataset live.')).toBeInTheDocument()
    expect(sendMock).not.toHaveBeenCalled() // nothing sent until the explicit confirmation

    await userEvent.click(within(dialog).getByRole('button', { name: /confirm & send to 12/i }))

    await waitFor(() => expect(sendMock).toHaveBeenCalledWith({
      subject: 'Season 2 opens', body: 'New dataset live.', audience: 'all', draftId: null,
      confirmedRecipientCount: 12,
    }))
    expect(await screen.findByText(/announcement sent to all active players/i)).toBeInTheDocument()
    expect(listMock).toHaveBeenCalledTimes(2) // history reloaded after the send
  })

  it('offers the draft-participants audience with a draft selector', async () => {
    previewMock.mockResolvedValue(previewResponse({
      audience: 'draft', draftId: 'd1', draftName: 'Tuesday Draft',
      audienceLabel: 'Participants of “Tuesday Draft”', recipientCount: 2, emailRecipientCount: 2, optedOutCount: 0,
    }))
    renderPage()

    await userEvent.type(await screen.findByLabelText(/subject/i), 'Ready?')
    await userEvent.type(screen.getByLabelText(/message/i), 'Kick-off soon.')
    await userEvent.click(screen.getByRole('radio', { name: /participants of a draft/i }))

    // A draft must be chosen before the preview unlocks.
    const previewButton = screen.getByRole('button', { name: /preview announcement/i })
    expect(previewButton).toBeDisabled()
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Draft' }), 'd1')
    expect(previewButton).toBeEnabled()

    await userEvent.click(previewButton)
    await screen.findByRole('dialog', { name: /review before sending/i })
    expect(previewMock).toHaveBeenCalledWith({
      subject: 'Ready?', body: 'Kick-off soon.', audience: 'draft', draftId: 'd1',
    })
  })

  it('explains a 409 as the audience moving since the preview', async () => {
    previewMock.mockResolvedValue(previewResponse())
    sendMock.mockRejectedValue(conflict())
    renderPage()

    await userEvent.type(await screen.findByLabelText(/subject/i), 'Season 2 opens')
    await userEvent.type(screen.getByLabelText(/message/i), 'New dataset live.')
    await userEvent.click(screen.getByRole('button', { name: /preview announcement/i }))
    const dialog = await screen.findByRole('dialog', { name: /review before sending/i })
    await userEvent.click(within(dialog).getByRole('button', { name: /confirm & send/i }))

    expect(await screen.findByText(/audience changed since your preview/i)).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('lists past campaigns with live delivery tallies', async () => {
    listMock.mockResolvedValue([announcement({ emailsPending: 3, emailsSent: 6, emailsFailed: 1 })])
    renderPage()

    const history = (await screen.findByRole('heading', { name: /sent announcements/i })).closest('section')!
    expect(within(history).getByText('Season 2 opens')).toBeInTheDocument()
    expect(within(history).getByText(/3 queued/)).toBeInTheDocument()
    expect(within(history).getByText(/6 sent/)).toBeInTheDocument()
    expect(within(history).getByText(/1 failed/)).toBeInTheDocument()
    expect(within(history).getByText(/2 opted out of email/)).toBeInTheDocument()
  })
})
