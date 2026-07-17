import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { AdminAuditLogPage } from './AdminAuditLogPage'
import { auditApi, draftsApi, usersApi } from '../services/api'
import type { DraftAuditEvent, SecurityAuditEvent } from '../types/admin'
import { detail } from '../test/draftFactories'

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return {
    ...actual,
    auditApi: { draftEvents: vi.fn(), securityEvents: vi.fn() },
    draftsApi: { ...actual.draftsApi, list: vi.fn() },
    usersApi: { ...actual.usersApi, list: vi.fn() },
  }
})

const draftEventsMock = auditApi.draftEvents as unknown as Mock
const securityMock = auditApi.securityEvents as unknown as Mock
const draftsMock = draftsApi.list as unknown as Mock
const usersMock = usersApi.list as unknown as Mock

function draftEvent(over?: Partial<DraftAuditEvent>): DraftAuditEvent {
  return {
    draftId: 'd1', draftName: 'Tuesday Draft', draftCode: 'ABC123', sequence: 14, type: 'DraftPaused',
    fromStatus: 'PositionDraft', toStatus: 'Paused', version: 15, actorUserId: 'admin-1',
    actorName: 'Devansh', reason: 'Connection dispute', createdAt: '2026-07-16T12:00:00Z', ...over,
  }
}

function securityEvent(over?: Partial<SecurityAuditEvent>): SecurityAuditEvent {
  return {
    id: 's1', action: 'AnnouncementSent', userId: 'admin-1', email: 'admin@draftroom.dev',
    detail: '“Season 2 opens” to All active players', ipAddress: '127.0.0.1',
    createdAt: '2026-07-16T12:00:00Z', ...over,
  }
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/admin/audit-log']}>
      <AdminAuditLogPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  draftsMock.mockResolvedValue([detail().summary])
  usersMock.mockResolvedValue({ items: [], page: 1, pageSize: 50, total: 0, totalPages: 1, invitedCount: 0, activatedCount: 0 })
  draftEventsMock.mockResolvedValue([draftEvent()])
  securityMock.mockResolvedValue([securityEvent()])
})

describe('AdminAuditLogPage (PR-21 §9.10)', () => {
  it('renders both immutable trails with attribution and reasons', async () => {
    renderPage()

    // Scope to table CELLS — the filter selects list the same type/action names as options.
    const draftPanel = (await screen.findByRole('heading', { name: /draft events/i })).closest('section')!
    expect(await within(draftPanel).findByRole('cell', { name: 'DraftPaused' })).toBeInTheDocument()
    expect(within(draftPanel).getByRole('cell', { name: 'PositionDraft → Paused' })).toBeInTheDocument()
    expect(within(draftPanel).getByRole('cell', { name: 'Devansh' })).toBeInTheDocument()
    expect(within(draftPanel).getByRole('cell', { name: 'Connection dispute' })).toBeInTheDocument()
    // The page states the append-only guarantee.
    expect(within(draftPanel).getByText(/never edited or deleted/i)).toBeInTheDocument()

    const securityPanel = (await screen.findByRole('heading', { name: /security & admin events/i })).closest('section')!
    expect(await within(securityPanel).findByRole('cell', { name: 'AnnouncementSent' })).toBeInTheDocument()
    expect(within(securityPanel).getByRole('cell', { name: 'admin@draftroom.dev' })).toBeInTheDocument()
    expect(within(securityPanel).getByRole('cell', { name: /Season 2 opens/ })).toBeInTheDocument()
  })

  it('re-queries the draft trail when the type filter changes', async () => {
    renderPage()
    await screen.findByText('DraftPaused')

    await userEvent.selectOptions(screen.getByLabelText(/event type/i), 'DraftCancelled')

    await waitFor(() => expect(draftEventsMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ type: 'DraftCancelled' })))
  })

  it('re-queries the security trail when the action filter changes', async () => {
    renderPage()
    await screen.findByText('AnnouncementSent')

    await userEvent.selectOptions(screen.getByLabelText(/action/i), 'AccountDeactivated')

    await waitFor(() => expect(securityMock).toHaveBeenLastCalledWith(
      expect.objectContaining({ action: 'AccountDeactivated' })))
  })

  it('shows empty states when the filters match nothing', async () => {
    draftEventsMock.mockResolvedValue([])
    securityMock.mockResolvedValue([])
    renderPage()

    expect(await screen.findByText(/no matching draft events/i)).toBeInTheDocument()
    expect(await screen.findByText(/no matching events/i)).toBeInTheDocument()
  })
})
