import { expect, type Browser, type BrowserContext, type Page } from '@playwright/test'
import type { Account } from './api'

// UI drivers for the full-stack multi-client suites (PR-23). Every selector is a role/label the
// real product renders — no test ids exist — so these helpers double as §12.4 linkage proof:
// if a control's accessible name regresses, the journey fails.

export type Client = { context: BrowserContext; page: Page; account: Account }

/** One isolated browser context per participant — a genuinely separate signed-in client. */
export async function newClient(browser: Browser, account: Account): Promise<Client> {
  const context = await browser.newContext({
    // Manual contexts do not inherit the config's `use` block. Reduced motion skips the spinner
    // wheel's decorative 2.2 s reveal (the committed order is identical either way — PR-13).
    reducedMotion: 'reduce',
    viewport: { width: 1280, height: 800 },
    baseURL: 'http://localhost:4174',
  })
  const page = await context.newPage()
  await signIn(page, account)
  return { context, page, account }
}

export async function signIn(page: Page, account: Account): Promise<void> {
  await page.goto('/login')
  await page.getByLabel('Email address').fill(account.email)
  // The password label wraps the eye toggle too, so target the textbox role directly.
  await page.getByRole('textbox', { name: /Password/ }).fill(account.password)
  await page.getByRole('button', { name: 'Enter draft room' }).click()
  // Landing on the dashboard proves the guard accepted the session.
  await expect(page.getByRole('link', { name: 'Drafts' }).first()).toBeVisible()
}

/**
 * Creates a lobby through the real New-lobby flow and returns the draft id parsed from the
 * lobby-detail URL the app navigates to.
 */
export async function createLobbyViaUi(
  page: Page,
  name: string,
  format: '1v1' | '2v2',
  inviteEmails: string[],
): Promise<string> {
  await page.getByRole('link', { name: 'Drafts' }).first().click()
  await page.getByRole('link', { name: 'New lobby' }).click()
  await page.getByRole('button', { name: format }).click()
  await page.getByLabel('Lobby name').fill(name)
  for (const email of inviteEmails) {
    await page.getByLabel('Search players to invite').fill(email)
    await page
      .getByRole('list', { name: 'People you can invite' })
      .getByRole('listitem')
      .filter({ hasText: email })
      .getByRole('button', { name: 'Invite' })
      .click()
  }
  await page.getByRole('button', { name: 'Create lobby' }).click()
  await page.waitForURL(/\/drafts\/[0-9a-f-]{36}$/)
  return page.url().split('/drafts/')[1]!
}

/** An invited participant opens the lobby and confirms presence. */
export async function confirmPresence(client: Client, draftId: string): Promise<void> {
  await client.page.goto(`/drafts/${draftId}`)
  await client.page.getByRole('button', { name: 'Confirm presence' }).click()
  await expect(client.page.getByText('Present').first()).toBeVisible()
}

/** Clicks the participant's own ready toggle (idempotent: no-op when already ready). */
export async function readyUp(page: Page): Promise<void> {
  const notReadyYet = page.getByRole('button', { name: "I'm ready", exact: true })
  if (await notReadyYet.isVisible().catch(() => false)) {
    await notReadyYet.click()
  }
  await expect(page.getByRole('button', { name: "I'm not ready", exact: true })).toBeVisible()
}

/** Host-side 2v2 formation: assign every seed, then pair the teams via the pair builder. */
export async function formTwoVsTwoTeams(
  host: Page,
  pairs: Array<{ seed1: string; seed2: string }>,
): Promise<void> {
  for (const pair of pairs) {
    // exact: true — "Seed for Practice Player" must not also match "…Player Two".
    await host.getByRole('group', { name: `Seed for ${pair.seed1}`, exact: true })
      .getByRole('button', { name: 'Seed 1', exact: true }).click()
    await host.getByRole('group', { name: `Seed for ${pair.seed2}`, exact: true })
      .getByRole('button', { name: 'Seed 2', exact: true }).click()
  }
  for (const pair of pairs) {
    await host.getByLabel('Seed 1 player').selectOption({ label: pair.seed1 })
    await host.getByLabel('Seed 2 player').selectOption({ label: pair.seed2 })
    await host.getByRole('button', { name: 'Add team' }).click()
  }
  await expect(
    host.getByRole('list', { name: 'Formed teams' }).getByRole('listitem'),
  ).toHaveCount(pairs.length)
}

/** Waits until one of the clients is on the club-selection clock and returns its page. */
async function waitForClubTurn(pages: Page[], timeoutMs = 30_000): Promise<Page> {
  const deadline = Date.now() + timeoutMs
  for (;;) {
    for (const page of pages) {
      const onClock = await page
        .getByText(/It's your turn — choose a five-star club/)
        .isVisible()
        .catch(() => false)
      if (onClock) return page
    }
    if (Date.now() > deadline) throw new Error('No client reached its club-selection turn in time')
    await pages[0]!.waitForTimeout(250)
  }
}

/**
 * Completes the entire club/protected-player round through the UI: whichever client is on the
 * clock chooses the first available five-star club and protects its first listed 75+ player,
 * confirming through the sheet (§9.5; no one-tap picks).
 */
export async function completeClubRoundViaUi(pages: Page[], teamCount: number): Promise<void> {
  for (let i = 0; i < teamCount; i++) {
    const active = await waitForClubTurn(pages)
    await active.getByLabel('Five-star club').selectOption({ index: 1 })
    await active
      .getByRole('list', { name: 'Players you can protect' })
      .getByRole('button', { name: 'Protect' })
      .first()
      .click()
    const dialog = active.getByRole('dialog')
    await expect(dialog.getByRole('heading', { name: 'Confirm protect' })).toBeVisible()
    await dialog.getByRole('button', { name: 'Confirm protect' }).click()
    await expect(dialog).toBeHidden()
  }
}

/** Waits until one client shows the exact "Your turn" chip; null when the draft completed. */
export async function waitForPickTurn(pages: Page[], timeoutMs = 45_000): Promise<Page | null> {
  const deadline = Date.now() + timeoutMs
  for (;;) {
    for (const page of pages) {
      if (await page.getByText('Your turn', { exact: true }).isVisible().catch(() => false)) {
        return page
      }
    }
    for (const page of pages) {
      if (await page.getByText('Draft complete').first().isVisible().catch(() => false)) {
        return null
      }
    }
    if (Date.now() > deadline) throw new Error('No client reached its pick turn in time')
    await pages[0]!.waitForTimeout(250)
  }
}

/** Drafts the first eligible player through the confirmation sheet on the active client. */
export async function pickFirstViaUi(page: Page): Promise<string> {
  const draftButton = page
    .getByRole('list', { name: 'Eligible players' })
    .getByRole('button', { name: /^Draft .+/ })
    .first()
  await expect(draftButton).toBeVisible()
  const label = (await draftButton.getAttribute('aria-label')) ?? ''
  const playerName = label.replace(/^Draft /, '')
  await draftButton.click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('heading', { name: 'Confirm draft' })).toBeVisible()
  await dialog.getByRole('button', { name: 'Confirm draft' }).click()
  await expect(dialog).toBeHidden()
  return playerName
}

/**
 * Plays the full snake position draft through the UI: whichever client's team is on the clock
 * confirms the top eligible player, until every squad slot is filled and the room announces
 * "Draft complete" on every client.
 */
export async function completePositionDraftViaUi(pages: Page[], totalPicks: number): Promise<void> {
  for (let i = 0; i < totalPicks; i++) {
    const active = await waitForPickTurn(pages)
    if (active === null) return // completed early (should not happen before totalPicks)
    await pickFirstViaUi(active)
  }
  for (const page of pages) {
    await expect(page.getByText('Draft complete').first()).toBeVisible({ timeout: 20_000 })
  }
}
