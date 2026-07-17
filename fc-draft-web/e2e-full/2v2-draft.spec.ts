import { expect, test } from '@playwright/test'
import { accounts, getDetail, login } from './helpers/api'
import {
  completeClubRoundViaUi,
  confirmPresence,
  createLobbyViaUi,
  formTwoVsTwoTeams,
  newClient,
  pickFirstViaUi,
  readyUp,
  waitForPickTurn,
} from './helpers/ui'

// PR-23 §17.9: the complete 2v2 acceptance journey (§16.3/4/5/6/7/9 evidence) across FOUR real
// browser clients — invite/join → host-assigned seeds → Seed1+Seed2 pairing → ready → spinner →
// club/protect round → full snake position draft with EITHER teammate submitting (DRAFT_RULES
// decision 6) → results. The pick loop deliberately rotates which teammate acts each turn.

test('a complete 2v2 draft: four clients, seeded pairs, either-teammate picks', async ({ browser }) => {
  const host = await newClient(browser, accounts.player)
  const mate = await newClient(browser, accounts.player2)
  const rival1 = await newClient(browser, accounts.player3)
  const rival2 = await newClient(browser, accounts.player4)
  const clients = [host, mate, rival1, rival2]
  const pages = clients.map((client) => client.page)

  const draftId = await test.step('host creates a 2v2 lobby inviting three players', () =>
    createLobbyViaUi(host.page, 'E2E 2v2 Cup', '2v2', [
      accounts.player2.email,
      accounts.player3.email,
      accounts.player4.email,
    ]))

  await test.step('all three invitees confirm presence; the host sees 4 present live', async () => {
    for (const client of [mate, rival1, rival2]) {
      await confirmPresence(client, draftId)
    }
    await expect(host.page.getByRole('heading', { name: /4 present · 4 in lobby/ })).toBeVisible()
  })

  await test.step('host locks and forms two Seed1+Seed2 teams', async () => {
    await host.page.getByRole('button', { name: 'Lock lobby & continue' }).click()
    await expect(host.page.getByRole('heading', { name: 'Seed and pair teams' })).toBeVisible()

    // §16.4: host-only seed assignment + pairing; non-hosts have no seed controls.
    await expect(mate.page.getByRole('button', { name: 'Seed 1', exact: true })).toHaveCount(0)

    await formTwoVsTwoTeams(host.page, [
      { seed1: accounts.player.name, seed2: accounts.player2.name },
      { seed1: accounts.player3.name, seed2: accounts.player4.name },
    ])

    // Every client sees the formed teams live.
    for (const page of pages) {
      await expect(
        page.getByRole('list', { name: 'Formed teams' }).getByRole('listitem'),
      ).toHaveCount(2)
    }
  })

  await test.step('everyone readies up and the host starts the draft', async () => {
    for (const client of clients) {
      await readyUp(client.page)
    }
    await host.page.getByRole('button', { name: 'Begin ready check' }).click()
    await host.page.getByRole('button', { name: 'Start draft' }).click()
    await expect(host.page.getByRole('heading', { name: 'Team order' })).toBeVisible()
  })

  await test.step('the spinner ranks both teams for every client', async () => {
    await host.page.getByRole('button', { name: 'Spin the wheel' }).click()
    for (const page of pages) {
      await expect(page.getByText('Order committed')).toBeVisible()
      await expect(
        page.getByRole('list', { name: 'Committed spinner order' }).getByRole('listitem'),
      ).toHaveCount(2)
    }
  })

  await test.step('each team locks its five-star club and protected player', async () => {
    await host.page.getByRole('button', { name: 'Open club selection' }).click()
    await completeClubRoundViaUi(pages, 2)
  })

  await test.step('the snake draft completes with alternating teammates submitting', async () => {
    await host.page.getByRole('button', { name: 'Open position draft' }).click()
    for (const page of pages) {
      // 2 held picks are already on the board, so the first position pick is 3 of 32.
      await expect(page.getByText(/Pick 3 of 32/)).toBeVisible()
    }

    for (let i = 0; i < 30; i++) {
      // Rotate the client order every turn so both members of the active team take turns being
      // the submitter — the UI proof of "either teammate may confirm; first valid wins".
      const rotated = pages.slice(i % pages.length).concat(pages.slice(0, i % pages.length))
      const active = await waitForPickTurn(rotated)
      if (active === null) break
      await pickFirstViaUi(active)
    }

    for (const page of pages) {
      await expect(page.getByText('Draft complete').first()).toBeVisible({ timeout: 20_000 })
    }
  })

  await test.step('results show two shared 16-player squads', async () => {
    await mate.page.getByRole('link', { name: 'View results & archive' }).click()
    await expect(mate.page.getByRole('group', { name: 'Teams' }).getByRole('button')).toHaveCount(2)
    await expect(
      mate.page.getByRole('list', { name: 'Pick sequence' }).getByRole('listitem'),
    ).toHaveCount(32)

    // Authoritative cross-check: both drafted squads are full and every pick unique.
    const session = await login(accounts.player)
    const detail = await getDetail(session, draftId)
    expect(detail.summary.status).toBe('Completed')
    expect(detail.teams).toHaveLength(2)
    for (const team of detail.teams) {
      expect(team.memberUserIds).toHaveLength(2)
      expect(detail.picks.filter((pick) => pick.teamId === team.id)).toHaveLength(16)
    }
    const footballerIds = detail.picks.map((pick) => pick.footballerId)
    expect(new Set(footballerIds).size).toBe(footballerIds.length)
  })

  for (const client of clients) {
    await client.context.close()
  }
})
