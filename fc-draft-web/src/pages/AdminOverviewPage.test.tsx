import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { AdminOverviewPage } from './AdminOverviewPage'
import { overviewApi } from '../services/api'
import type { AdminOverview } from '../types/admin'

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return { ...actual, overviewApi: { get: vi.fn() } }
})

const overviewMock = overviewApi.get as unknown as Mock

function overview(over?: Partial<AdminOverview>): AdminOverview {
  return {
    users: { total: 12, activated: 9, awaitingActivation: 3, invited: 10 },
    drafts: { total: 5, live: 1, completed: 3, cancelled: 1, oneVOne: 2, twoVTwo: 3, byStatus: { PositionDraft: 1, Completed: 3, Cancelled: 1 } },
    engagement: { created: 5, started: 4, completed: 3, lobbyToStartRate: 0.8, completionRate: 0.75, picksAccepted: 96, autoPicks: 6, autoPickRate: 0.0625 },
    email: { pending: 0, sent: 40, failed: 2 },
    alerts: [{ severity: 'warning', message: '2 email(s) failed to deliver — review the Communications outbox.' }],
    generatedAt: '2026-07-17T10:00:00Z',
    ...over,
  }
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/admin/overview']}>
      <AdminOverviewPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  overviewMock.mockResolvedValue(overview())
})

describe('AdminOverviewPage (§8.2)', () => {
  it('renders user, draft, and engagement summaries with derived rates', async () => {
    renderPage()

    expect(await screen.findByRole('heading', { name: 'Overview' })).toBeInTheDocument()
    // Headline tiles.
    const accounts = screen.getByRole('region', { name: 'Accounts' })
    expect(accounts).toHaveTextContent('12')
    expect(accounts).toHaveTextContent('Awaiting activation')
    // Conversion rates are formatted as percentages.
    expect(screen.getByText('Lobby → draft start').closest('div')).toHaveTextContent('80%')
    expect(screen.getByText('Draft completion').closest('div')).toHaveTextContent('75%')
    expect(screen.getByText('Format split').closest('div')).toHaveTextContent('2 × 1v1 · 3 × 2v2')
    // Drafts-by-status uses friendly labels.
    expect(screen.getByText('Position draft')).toBeInTheDocument()
    // Email delivery health.
    expect(screen.getByText(/2 failed/)).toBeInTheDocument()
  })

  it('surfaces the alerts strip', async () => {
    renderPage()
    const alerts = await screen.findByRole('list', { name: 'Alerts' })
    expect(alerts).toHaveTextContent(/2 email\(s\) failed to deliver/)
  })

  it('shows an all-clear when there are no alerts', async () => {
    overviewMock.mockResolvedValue(overview({ alerts: [] }))
    renderPage()
    expect(await screen.findByText(/everything looks healthy/i)).toBeInTheDocument()
  })

  it('refreshes on demand', async () => {
    renderPage()
    await screen.findByRole('heading', { name: 'Overview' })
    expect(overviewMock).toHaveBeenCalledTimes(1)

    await userEvent.click(screen.getByRole('button', { name: /refresh/i }))
    await waitFor(() => expect(overviewMock).toHaveBeenCalledTimes(2))
  })
})
