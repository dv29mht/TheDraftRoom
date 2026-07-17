import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import axe from 'axe-core'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'
import { LoginPage } from '../pages/LoginPage'
import { DraftsHubPage } from '../pages/DraftsHubPage'
import { ProfilePage } from '../pages/ProfilePage'
import { DraftRoomStage } from '../components/draft/DraftRoomStage'
import { draftsApi, meApi } from '../services/api'
import { useAuthStore } from '../stores/authStore'
import { HOST, board, detail, runningTimer } from './draftFactories'

// PR-22 (§17.9 done-when): automated accessibility checks over the core journeys — sign-in, the
// draft hub, the live draft room, and the profile. These jsdom scans run every WCAG 2.2 rule axe
// can evaluate without a layout engine; color-contrast (needs real rendering) is covered by the
// Playwright axe pass in e2e/accessibility.spec.ts, and the page-scope landmark rules are skipped
// because components render here without the surrounding AppShell landmarks.
async function expectNoViolations(container: Element) {
  const results = await axe.run(container, {
    rules: {
      'color-contrast': { enabled: false },
      region: { enabled: false },
      'landmark-one-main': { enabled: false },
      'page-has-heading-one': { enabled: false }
    }
  })
  expect(
    results.violations.map((violation) => `${violation.id}: ${violation.nodes[0]?.html}`)
  ).toEqual([])
}

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return {
    ...actual,
    draftsApi: { ...actual.draftsApi, list: vi.fn(), board: vi.fn() },
    meApi: { ...actual.meApi, emailPreferences: vi.fn() }
  }
})

const listMock = draftsApi.list as unknown as Mock
const boardMock = draftsApi.board as unknown as Mock
const preferencesMock = meApi.emailPreferences as unknown as Mock

describe('core journeys pass automated accessibility checks (axe)', () => {
  beforeEach(() => {
    useAuthStore.setState({ user: HOST, accessToken: 'token', mustChangePassword: false })
  })

  it('sign-in page', async () => {
    const { container } = render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>
    )
    await expectNoViolations(container)
  })

  it('draft hub with drafts and the install card', async () => {
    listMock.mockResolvedValue([
      {
        id: 'd1', code: 'ABC123', name: 'Tuesday Draft', format: '1v1', status: 'Lobby',
        hostUserId: HOST.id, version: 1, pickTimerSeconds: 120, pinnedDatasetVersionId: null,
        participantCount: 2, createdAt: '2026-07-15T00:00:00Z', startedAt: null, completedAt: null
      }
    ])
    const { container } = render(
      <MemoryRouter>
        <DraftsHubPage />
      </MemoryRouter>
    )
    await screen.findByText('Tuesday Draft')
    await expectNoViolations(container)
  })

  it('live draft room on the clock', async () => {
    boardMock.mockResolvedValue(
      board({
        status: 'PositionDraft',
        isMyTurn: true,
        eligibleFootballers: [
          { id: 700, name: 'Harry Kane', overall: 90, clubId: 'c3', clubName: 'Bayern', positions: ['ST'] }
        ]
      })
    )
    const roomDetail = detail({
      status: 'PositionDraft',
      teams: [
        { id: 't1', name: 'Host One', spinnerRank: 1, selectedClubId: 'c1', selectedClubName: 'Real Madrid', memberUserIds: [HOST.id] }
      ],
      slots: [
        { order: 0, slotType: 'Held', position: null, label: 'Held player' },
        { order: 1, slotType: 'StartingPosition', position: 'ST', label: 'ST' }
      ],
      turn: {
        phase: 'PositionDraft', direction: 'Ascending', round: 1,
        activeTeamId: 't1', activeTeamName: 'Host One', activeTeamMemberUserIds: [HOST.id],
        activeSlotOrder: 1, activeSlotLabel: 'ST', activeSlotPosition: 'ST', slotAcceptsAnyPosition: false
      },
      timer: runningTimer(90)
    })
    const { container } = render(
      <MemoryRouter>
        <DraftRoomStage detail={roomDetail} busy={false} userId={HOST.id} hubStatus="connected" mutate={vi.fn()} />
      </MemoryRouter>
    )
    await screen.findByText('Harry Kane')
    await expectNoViolations(container)
  })

  it('profile with install guidance', async () => {
    preferencesMock.mockResolvedValue({ optionalEmailOptOut: false })
    const { container } = render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )
    await screen.findByText(/optional announcements/i)
    await expectNoViolations(container)
  })
})
