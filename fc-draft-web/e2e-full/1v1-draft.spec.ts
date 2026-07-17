import { expect, test } from '@playwright/test'
import { accounts, getResults, login } from './helpers/api'
import {
  completeClubRoundViaUi,
  completePositionDraftViaUi,
  confirmPresence,
  createLobbyViaUi,
  newClient,
  pickFirstViaUi,
  readyUp,
  waitForPickTurn,
} from './helpers/ui'

// PR-23 §17.9: the complete 1v1 acceptance journey (§16.3/5/6/7/9/11 evidence) driven through TWO
// real browser clients against the full stack — lobby → invite/join → ready → start → spinner →
// club/protect round → full 30-pick snake position draft → results. Every state change one client
// makes must appear on the other WITHOUT reloading (SignalR §9.6/§14); REST is used only to
// cross-check the server's authoritative record at the end.

test('a complete 1v1 draft: two clients, lobby to results', async ({ browser }) => {
  const host = await newClient(browser, accounts.player)
  const guest = await newClient(browser, accounts.player2)
  const pages = [host.page, guest.page]

  const draftId = await test.step('host creates the lobby and invites the guest', () =>
    createLobbyViaUi(host.page, 'E2E 1v1 Championship', '1v1', [accounts.player2.email]))

  await test.step('guest confirms presence; the host sees it live', async () => {
    await confirmPresence(guest, draftId)
    // §16.9: presence propagates to the host without a reload.
    await expect(host.page.getByRole('heading', { name: /2 present · 2 in lobby/ })).toBeVisible()
  })

  await test.step('host locks the lobby into team formation', async () => {
    await host.page.getByRole('button', { name: 'Lock lobby & continue' }).click()
    await expect(host.page.getByRole('heading', { name: 'Confirm solo teams' })).toBeVisible()
    await expect(guest.page.getByRole('heading', { name: 'Confirm solo teams' })).toBeVisible()
  })

  await test.step('solo teams form and both players ready up', async () => {
    await host.page.getByRole('button', { name: 'Form solo teams' }).click()
    await expect(
      host.page.getByRole('list', { name: 'Formed teams' }).getByRole('listitem'),
    ).toHaveCount(2)
    await readyUp(guest.page)
    await readyUp(host.page)
  })

  await test.step('only the host can start; the spinner commits one order for every client', async () => {
    // §16.5: the guest never sees host-only controls.
    await expect(guest.page.getByRole('button', { name: 'Begin ready check' })).toHaveCount(0)
    await host.page.getByRole('button', { name: 'Begin ready check' }).click()
    await expect(host.page.getByRole('heading', { name: 'Confirm and start' })).toBeVisible()
    await expect(guest.page.getByRole('button', { name: 'Start draft' })).toHaveCount(0)
    await host.page.getByRole('button', { name: 'Start draft' }).click()

    await expect(host.page.getByRole('heading', { name: 'Team order' })).toBeVisible()
    await expect(guest.page.getByRole('button', { name: 'Spin the wheel' })).toHaveCount(0)
    await host.page.getByRole('button', { name: 'Spin the wheel' }).click()
    // Both clients see the SAME committed order (§9.5), the guest via live push.
    for (const page of pages) {
      await expect(page.getByText('Order committed')).toBeVisible()
      await expect(
        page.getByRole('list', { name: 'Committed spinner order' }).getByRole('listitem'),
      ).toHaveCount(2)
    }
  })

  await test.step('both teams choose a five-star club and protect a player, in order', async () => {
    await host.page.getByRole('button', { name: 'Open club selection' }).click()
    await completeClubRoundViaUi(pages, 2)
    // §16.6: every team's protected player is locked before positions open.
    await expect(host.page.getByText(/Protected: /).first()).toBeVisible()
  })

  await test.step('the position draft opens at ST with the 120s clock visible to all', async () => {
    await host.page.getByRole('button', { name: 'Open position draft' }).click()
    for (const page of pages) {
      // §16.7: the first slot on the clock is ST and the server clock renders. The progress
      // counter includes the two held picks already made in the club round: pick 3 of 32.
      await expect(page.getByRole('heading', { name: / · ST$/ })).toBeVisible()
      await expect(page.getByRole('timer')).toBeVisible()
      await expect(page.getByText(/Pick 3 of 32/)).toBeVisible()
    }
  })

  await test.step('the first pick propagates live to the waiting client', async () => {
    const active = await waitForPickTurn(pages)
    expect(active).not.toBeNull()
    const waiting = active === host.page ? guest.page : host.page
    await pickFirstViaUi(active!)
    // §14/§16.9: the other client advances without any reload.
    await expect(waiting.getByText(/Pick 4 of 32/)).toBeVisible()
  })

  await test.step('the full snake draft completes across both clients', async () => {
    await completePositionDraftViaUi(pages, 29) // 29 remaining of 30
  })

  await test.step('both participants open identical, immutable results', async () => {
    for (const page of pages) {
      await page.getByRole('link', { name: 'View results & archive' }).click()
      await expect(page.getByRole('group', { name: 'Teams' }).getByRole('button')).toHaveCount(2)
      await expect(
        page.getByRole('list', { name: 'Pick sequence' }).getByRole('listitem'),
      ).toHaveCount(32) // 2 held + 30 position picks
      await page.getByRole('button', { name: 'List', exact: true }).click()
      await expect(page.getByText('Held').first()).toBeVisible()
    }

    // Cross-check the authoritative record: both squads hold 16 footballers.
    const session = await login(accounts.player)
    const results = await getResults(session, draftId)
    expect(results.teams).toHaveLength(2)
    for (const team of results.teams) {
      expect(team.picks).toHaveLength(16)
      expect(team.averageOverall).toBeGreaterThanOrEqual(75)
    }
  })

  await host.context.close()
  await guest.context.close()
})
