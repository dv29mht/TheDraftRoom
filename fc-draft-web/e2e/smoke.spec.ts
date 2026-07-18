import { expect, test } from '@playwright/test'

// The app shows a ~900ms brand loader before the router mounts; Playwright's auto-waiting
// assertions ride past it, so no explicit sleeps are needed.

test('login screen renders with empty credential fields', async ({ page }) => {
  await page.goto('/login')

  await expect(page.getByRole('heading', { name: /enter the draft room/i })).toBeVisible()
  await expect(page.getByLabel(/email address/i)).toHaveValue('')
  await expect(page.getByRole('button', { name: /enter draft room/i })).toBeVisible()
})

test('an anonymous visitor to a protected route is redirected to login', async ({ page }) => {
  await page.goto('/')

  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByRole('heading', { name: /enter the draft room/i })).toBeVisible()
})

// The PR-21 admin modules sit behind the same guards: anonymous deep links land on login.
for (const route of ['/admin/communications', '/admin/audit-log', '/admin/drafts']) {
  test(`an anonymous visitor to ${route} is redirected to login`, async ({ page }) => {
    await page.goto(route)

    await expect(page).toHaveURL(/\/login$/)
    await expect(page.getByRole('heading', { name: /enter the draft room/i })).toBeVisible()
  })
}

test('the installable PWA manifest is served', async ({ page, request }) => {
  await page.goto('/login')
  const manifestHref = await page.locator('link[rel="manifest"]').getAttribute('href')
  expect(manifestHref).toBeTruthy()

  const manifest = await request.get(manifestHref!)
  expect(manifest.ok()).toBeTruthy()
  expect((await manifest.json()).name).toBe('ROSTR')
})
