import { defineConfig, devices } from '@playwright/test'

// Smoke-test scaffolding. These specs drive the built PWA shell served by `vite preview`
// and only exercise client-side behaviour (render + route guards), so they need no API and
// stay deterministic. Full-stack end-to-end flows (real login, room creation) are covered by
// the .NET integration suite today and can be layered on here as the draft engine lands.
const PORT = 4173
const baseURL = `http://localhost:${PORT}`

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? [['github'], ['list']] : 'list',
  use: {
    baseURL,
    trace: 'on-first-retry',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  webServer: {
    command: `npm run build && npm run preview -- --port ${PORT} --strictPort`,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
})
