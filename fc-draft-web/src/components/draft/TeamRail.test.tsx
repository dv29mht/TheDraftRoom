import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { TeamRail } from './TeamRail'
import { GUEST, HOST, detail } from '../../test/draftFactories'
import type { DraftDetail, DraftPick, DraftTeam } from '../../types/draft'

const teams: DraftTeam[] = [
  { id: 't1', name: 'Host One', spinnerRank: 1, selectedClubId: 'c1', selectedClubName: 'Real Madrid', memberUserIds: [HOST.id] },
  { id: 't2', name: 'Guest One', spinnerRank: 2, selectedClubId: 'c2', selectedClubName: 'Arsenal', memberUserIds: [GUEST.id] },
]

const slots = [
  { order: 0, slotType: 'Held', position: null, label: 'Held player' },
  { order: 1, slotType: 'StartingPosition', position: 'ST', label: 'ST' },
  { order: 2, slotType: 'StartingPosition', position: 'LW', label: 'LW' },
]

function pick(teamId: string, slotOrder: number, name: string): DraftPick {
  return { teamId, slotOrder, footballerId: name.length * 100 + slotOrder, footballerName: name, footballerOverall: 85, footballerPosition: null, pickedByParticipantId: null }
}

// Held round straight (t1, t2); round 1 ascending (t1, t2); round 2 descending (t2, t1).
const picks: DraftPick[] = [
  pick('t2', 1, 'R1 Second'),
  pick('t1', 0, 'Held First'),
  pick('t2', 2, 'R2 First'),
  pick('t1', 1, 'R1 First'),
  pick('t2', 0, 'Held Second'),
  pick('t1', 2, 'R2 Second'),
]

function railDetail(): DraftDetail {
  return detail({
    status: 'PositionDraft',
    teams,
    slots,
    picks,
    turn: {
      phase: 'PositionDraft', direction: 'Descending', round: 2,
      activeTeamId: 't2', activeTeamName: 'Guest One', activeTeamMemberUserIds: [GUEST.id],
      activeSlotOrder: 2, activeSlotLabel: 'LW', activeSlotPosition: 'LW', slotAcceptsAnyPosition: false,
    },
  })
}

describe('TeamRail', () => {
  it('focuses my squad by default and switches when another team chip is selected', async () => {
    render(<TeamRail detail={railDetail()} userId={GUEST.id} />)

    // The viewer is on Guest One — their squad is focused first, with the on-the-clock marker on the rail.
    expect(screen.getByLabelText('Guest One squad')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /guest one · you/i })).toHaveClass('is-on-clock')

    await userEvent.click(screen.getByRole('button', { name: /host one/i }))
    expect(screen.getByLabelText('Host One squad')).toBeInTheDocument()
    expect(within(screen.getByLabelText('Host One squad')).getByText(/held first/i)).toBeInTheDocument()
  })

  it('reconstructs the chronological pick order (straight held round, then snake) for recent and history views', async () => {
    render(<TeamRail detail={railDetail()} userId={HOST.id} />)

    await userEvent.click(screen.getByRole('button', { name: /full history/i }))
    const history = screen.getByRole('list', { name: /full pick history/i })
    const names = within(history).getAllByRole('listitem').map((row) => row.textContent)
    expect(names[0]).toContain('Held First')
    expect(names[1]).toContain('Held Second')
    expect(names[2]).toContain('R1 First')
    expect(names[3]).toContain('R1 Second')
    expect(names[4]).toContain('R2 First') // round 2 reverses: rank 2 picks first
    expect(names[5]).toContain('R2 Second')

    await userEvent.click(screen.getByRole('button', { name: /recent picks/i }))
    const recent = screen.getByRole('list', { name: /recent picks/i })
    const recentRows = within(recent).getAllByRole('listitem')
    expect(recentRows[0]).toHaveTextContent('R2 Second') // most recent first
    expect(recentRows[0]).toHaveClass('is-newest')
  })
})
