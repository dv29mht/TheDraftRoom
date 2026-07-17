import { expect, test, type Page } from '@playwright/test'
import {
  accounts,
  driveToPositionDraft,
  getBoard,
  getDetail,
  login,
  pickFirstEligible,
} from './helpers/api'
import { newClient, pickFirstViaUi, waitForPickTurn } from './helpers/ui'

// PR-23 §17.9 concurrency/race + resilience, END TO END through real clients (§16.10/§16.11/
// §16.13, §6.5, §7.4, §18). The REST driver fast-forwards fresh drafts to the stage under test —
// the documented create → … → open-positions sequence — and the scenarios themselves play out in
// real browser sessions against the live SignalR channel.

const API = process.env.DRAFT_API_URL ?? 'http://127.0.0.1:5089'

async function api<T>(method: string, path: string, token: string, body?: unknown): Promise<T> {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) throw new Error(`${method} ${path} -> ${response.status}`)
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

const positionPicks = (detail: Awaited<ReturnType<typeof getDetail>>) =>
  detail.picks.filter((pick) => pick.slotOrder > 0)

test('simultaneous 2v2 teammate submissions: first valid wins, the loser resyncs with an explanation', async ({ browser }) => {
  // Fast-forward a fresh 2v2 draft to a live position draft over REST.
  const sessions = await Promise.all(
    [accounts.player, accounts.player2, accounts.player3, accounts.player4].map(login),
  )
  const { draftId } = await driveToPositionDraft('E2E Race 2v2', '2v2', sessions)

  // Open REAL clients for both members of the team on the clock.
  const board = await getBoard(sessions[0]!, draftId)
  const activeAccounts = Object.values(accounts).filter((account) =>
    board.turn.activeTeamMemberUserIds.includes(
      sessions.find((session) => session.email === account.email)?.userId ?? '',
    ),
  )
  expect(activeAccounts).toHaveLength(2)
  const teammateA = await newClient(browser, activeAccounts[0]!)
  const teammateB = await newClient(browser, activeAccounts[1]!)
  await teammateA.page.goto(`/drafts/${draftId}`)
  await teammateB.page.goto(`/drafts/${draftId}`)
  await expect(teammateA.page.getByText('Your turn', { exact: true })).toBeVisible()
  await expect(teammateB.page.getByText('Your turn', { exact: true })).toBeVisible()

  // Each teammate stages a DIFFERENT player in the confirmation sheet…
  const stage = async (page: Page, index: number) => {
    const button = page
      .getByRole('list', { name: 'Eligible players' })
      .getByRole('button', { name: /^Draft .+/ })
      .nth(index)
    const label = (await button.getAttribute('aria-label')) ?? ''
    await button.click()
    await expect(page.getByRole('dialog').getByRole('heading', { name: 'Confirm draft' })).toBeVisible()
    return label.replace(/^Draft /, '')
  }
  const choiceA = await stage(teammateA.page, 0)
  const choiceB = await stage(teammateB.page, 1)
  expect(choiceA).not.toBe(choiceB)

  // …then both confirm at the same instant.
  await Promise.all([
    teammateA.page.getByRole('dialog').getByRole('button', { name: 'Confirm draft' }).click(),
    teammateB.page.getByRole('dialog').getByRole('button', { name: 'Confirm draft' }).click(),
  ])

  // §6.5/§16.10: the server accepted EXACTLY one pick for the slot.
  await expect
    .poll(async () => positionPicks(await getDetail(sessions[0]!, draftId)).length)
    .toBe(1)
  const accepted = positionPicks(await getDetail(sessions[0]!, draftId))[0]!
  expect([choiceA, choiceB]).toContain(accepted.footballerName)

  // The losing client explains the race and resyncs; both clients converge on the next pick
  // (2 held + 1 position pick made → pick 4 of 32).
  const loser = accepted.footballerName === choiceA ? teammateB.page : teammateA.page
  await expect(loser.getByRole('alert')).toContainText(/acted first|turn|moved on/i)
  await expect(teammateA.page.getByText(/Pick 4 of 32/)).toBeVisible()
  await expect(teammateB.page.getByText(/Pick 4 of 32/)).toBeVisible()

  await teammateA.context.close()
  await teammateB.context.close()
})

test('a disconnected participant reconnects mid-draft to authoritative state with no duplicate picks', async ({ browser }) => {
  const sessions = await Promise.all([accounts.player3, accounts.player4].map(login))
  const { draftId } = await driveToPositionDraft('E2E Reconnect 1v1', '1v1', sessions)

  const client = await newClient(browser, accounts.player3)
  await client.page.goto(`/drafts/${draftId}`)
  await expect(client.page.getByText('Live', { exact: true })).toBeVisible()

  // The connection drops mid-draft (§7.4): the app shows an explicit offline state.
  await client.context.setOffline(true)
  await expect(client.page.getByText(/you.re offline/i)).toBeVisible()

  // The draft moves on without this client: four picks land through other connections.
  let detail = await getDetail(sessions[0]!, draftId)
  for (let i = 0; i < 4; i++) {
    detail = await pickFirstEligible(sessions, draftId, detail)
  }
  expect(positionPicks(detail)).toHaveLength(4)

  // Reconnect: the client rejoins the live group and reconciles from the authoritative
  // snapshot — showing all four missed picks, duplicating nothing (§16.11).
  await client.context.setOffline(false)
  await expect(client.page.getByText('Live', { exact: true })).toBeVisible({ timeout: 45_000 })
  // 2 held + 4 position picks landed while offline → the room resumes at pick 7 of 32.
  await expect(client.page.getByText(/Pick 7 of 32/)).toBeVisible()

  // The reconnected participant keeps playing: REST-advance until it is their team's turn,
  // then pick through the real UI.
  for (let i = 0; i < 2; i++) {
    const board = await getBoard(sessions[0]!, draftId)
    if (board.turn.activeTeamMemberUserIds.includes(sessions[0]!.userId)) break
    detail = await pickFirstEligible(sessions, draftId, detail)
  }
  const active = await waitForPickTurn([client.page])
  expect(active).not.toBeNull()
  const before = positionPicks(await getDetail(sessions[0]!, draftId)).length
  await pickFirstViaUi(active!)
  await expect
    .poll(async () => positionPicks(await getDetail(sessions[0]!, draftId)).length)
    .toBe(before + 1) // exactly one new pick — nothing replayed or duplicated

  await client.context.close()
})

test('a Brevo outage never blocks the mutation: cancellation commits, clients see it live, delivery status is visible', async ({ browser }) => {
  // Environment Testing runs with Brevo deliberately unconfigured — every send attempt fails
  // exactly like an outage. The draft mutation must commit anyway (§9.8, §16.13).
  const sessions = await Promise.all([accounts.player3, accounts.player4].map(login))
  const { draftId, detail } = await driveToPositionDraft('E2E Outage 1v1', '1v1', sessions)

  const hostClient = await newClient(browser, accounts.player3)
  const guestClient = await newClient(browser, accounts.player4)
  await hostClient.page.goto(`/drafts/${draftId}`)
  await guestClient.page.goto(`/drafts/${draftId}`)
  await expect(hostClient.page.getByText(/Pick 3 of 32/)).toBeVisible()

  // The host cancels with a reason through the real control panel.
  await hostClient.page
    .getByLabel('Reason for pausing or cancelling')
    .fill('E2E outage drill — cancelling mid-draft')
  await hostClient.page.getByRole('button', { name: 'Cancel draft' }).click()

  // Both clients land on the terminal stage — the guest via live push.
  for (const page of [hostClient.page, guestClient.page]) {
    await expect(page.getByRole('heading', { name: 'This draft has ended' })).toBeVisible()
    await expect(page.getByText(/Draft cancelled — E2E outage drill/)).toBeVisible()
  }

  // The cancellation's in-app notifications landed despite the email failure…
  const guestNotifications = await api<{ items: Array<{ type: string; draftId: string }> }>(
    'GET', '/api/me/notifications', sessions[1]!.token)
  expect(
    guestNotifications.items.some(
      (item) => item.draftId === draftId && item.type.includes('cancel'),
    ),
  ).toBe(true)

  // …and the admin delivery view records the failed attempts instead of hiding them (§9.8).
  const admin = await login(accounts.admin)
  const outbox = await api<Array<{ kind: string; status: string; lastError: string | null }>>(
    'GET', '/api/admin/email-outbox', admin.token)
  const cancelledEmails = outbox.filter((entry) => entry.kind === 'DraftCancelled')
  expect(cancelledEmails.length).toBeGreaterThanOrEqual(2)
  for (const entry of cancelledEmails) {
    expect(entry.status).toBe('Failed')
    expect(entry.lastError ?? '').toContain('Brevo is not configured')
  }

  // The server state is Cancelled — committed, versioned, and auditable.
  const finalDetail = await getDetail(sessions[0]!, draftId)
  expect(finalDetail.summary.status).toBe('Cancelled')
  expect(finalDetail.summary.version).toBeGreaterThan(detail.summary.version)
  expect(finalDetail.events.some((event) => event.type === 'DraftCancelled')).toBe(true)

  await hostClient.context.close()
  await guestClient.context.close()
})
