import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'

// PR-22 release hardening (§17.9): automated accessibility over the real rendered shell (both
// themes, real color-contrast), the §12.2 offline state, 375px/landscape layout sanity, and the
// service-worker no-API-cache proof.

const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']

async function expectNoWcagViolations(page: import('@playwright/test').Page) {
  const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze()
  expect(
    results.violations.map((violation) => `${violation.id}: ${violation.nodes[0]?.html}`)
  ).toEqual([])
}

for (const theme of ['light', 'dark'] as const) {
  test(`sign-in journey passes axe WCAG 2.2 AA checks in ${theme} mode`, async ({ page }) => {
    if (theme === 'dark') {
      await page.addInitScript(() =>
        localStorage.setItem('draft-room-theme', JSON.stringify({ state: { theme: 'dark' }, version: 0 }))
      )
    }
    await page.goto('/login')
    await expect(page.getByRole('heading', { name: /enter the draft room/i })).toBeVisible()
    if (theme === 'dark') {
      await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
    }
    await expectNoWcagViolations(page)
  })
}

test('password-recovery page passes axe WCAG 2.2 AA checks', async ({ page }) => {
  await page.goto('/forgot-password')
  await expect(page.getByRole('heading')).toBeVisible()
  await expectNoWcagViolations(page)
})

test('going offline shows the blocking banner; reconnecting clears it', async ({ page, context }) => {
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: /enter the draft room/i })).toBeVisible()

  await context.setOffline(true)
  const banner = page.getByRole('status').filter({ hasText: /you.re offline/i })
  await expect(banner).toBeVisible()

  await context.setOffline(false)
  await expect(banner).toBeHidden()
})

test('375px viewport shows no horizontal page scroll on the sign-in journey', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 667 })
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: /enter the draft room/i })).toBeVisible()

  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  )
  expect(overflow).toBe(0)
})

test('landscape phone viewport keeps the sign-in form usable without horizontal scroll', async ({ page }) => {
  await page.setViewportSize({ width: 844, height: 390 })
  await page.goto('/login')

  await expect(page.getByLabel(/email address/i)).toBeVisible()
  await expect(page.getByRole('button', { name: /enter draft room/i })).toBeVisible()
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  )
  expect(overflow).toBe(0)
})

test('sign-in form is keyboard operable', async ({ page }) => {
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: /enter the draft room/i })).toBeVisible()

  // Tab through the page: the email field, password field, its show-password control, and the
  // submit button must all be reachable without a pointer.
  const reached = new Set<string>()
  for (let i = 0; i < 12; i += 1) {
    await page.keyboard.press('Tab')
    const label = await page.evaluate(() => {
      const active = document.activeElement as HTMLElement | null
      return active?.getAttribute('aria-label') ?? active?.getAttribute('name') ?? active?.textContent?.trim() ?? ''
    })
    if (label) reached.add(label)
  }
  expect([...reached].join(' | ')).toMatch(/email/i)
  expect([...reached].join(' | ')).toMatch(/password/i)
  expect([...reached].join(' | ')).toMatch(/enter draft room/i)
})

test('the generated service worker never caches the API (§12.2, §18)', async () => {
  // The webServer build step regenerates dist/sw.js before this suite runs.
  const swSource = readFileSync(resolve(process.cwd(), 'dist/sw.js'), 'utf8')

  // 1. The precache manifest contains no API/hub/health URL.
  const precacheUrls = [...swSource.matchAll(/url:"([^"]+)"/g)].map((match) => match[1])
  expect(precacheUrls.length).toBeGreaterThan(0)
  expect(precacheUrls.filter((url) => /^(api|hubs|health)\b/.test(url.replace(/^\//, '')))).toEqual([])

  // 2. Navigation fallback explicitly denylists /api, /hubs, /health and /swagger.
  expect(swSource).toContain('denylist:[/^\\/api\\//,/^\\/hubs\\//,/^\\/health$/,/^\\/swagger/]')

  // 3. No runtime caching route exists at all — the only fetch strategy is the precached shell.
  expect(swSource).not.toContain('NetworkFirst')
  expect(swSource).not.toContain('StaleWhileRevalidate')
  expect(swSource).not.toContain('CacheFirst')
})
