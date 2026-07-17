import { defineConfig, devices } from '@playwright/test'

// Full-stack multi-client E2E (PR-23, PRD §17.9/§16). Unlike playwright.config.ts (client-only
// shell checks), this config boots the REAL stack: the .NET API in environment **Testing** —
// in-memory branch, seeded deterministic + demo accounts, Brevo deliberately unconfigured so no
// live email can ever be sent (never environment Development, whose gitignored settings hold a
// real key) — behind the production `vite preview` build with its /api + /hubs proxy (PR-22's
// seam). Dedicated ports (5089/4174) keep it clear of a developer's running dev stack (5088/4173).
//
// The suites drive real multi-client browser sessions through complete 1v1 and 2v2 drafts, so the
// runs are long and strictly serialized: draft state is server-side and the in-memory store is
// shared across specs — one worker, no parallelism.
const API_PORT = 5089
const WEB_PORT = 4174
const apiOrigin = `http://127.0.0.1:${API_PORT}`
const baseURL = `http://localhost:${WEB_PORT}`

export default defineConfig({
  testDir: './e2e-full',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  // A full draft journey is not worth auto-retrying wholesale; failures should be investigated.
  retries: 0,
  workers: 1,
  reporter: process.env.CI ? [['github'], ['list']] : 'list',
  // A complete UI-driven snake draft (30 confirmed picks across clients) needs room; individual
  // expectations stay tight enough to catch real stalls (§14 propagation is asserted separately).
  timeout: 420_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      // Testing environment: appsettings.Development.json is NOT loaded (no real Brevo key, no
      // connection string) → the in-memory branch with the seeded accounts; the demo players give
      // the 2v2 suite its 4 activated participants.
      command: `dotnet run --project ../src/FcDraft.API/FcDraft.API.csproj --no-launch-profile`,
      url: `${apiOrigin}/health`,
      reuseExistingServer: !process.env.CI,
      timeout: 240_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Testing',
        ASPNETCORE_URLS: apiOrigin,
        Database__SeedDemoAccounts: 'true',
      },
    },
    {
      command: `npm run build && npm run preview -- --port ${WEB_PORT} --strictPort`,
      url: baseURL,
      reuseExistingServer: !process.env.CI,
      timeout: 240_000,
      env: {
        DRAFT_API_ORIGIN: apiOrigin,
      },
    },
  ],
})
