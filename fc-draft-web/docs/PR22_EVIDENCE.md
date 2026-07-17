# PR-22 — Release-hardening evidence (16 July 2026)

Verification record for PRD §17.9's done-when: measured §14 numbers, the automated
accessibility runs, and the manual review matrix. Screenshots live in
[`assets/pr22-evidence/`](assets/pr22-evidence/) and were captured from the REAL app — the
production build (`vite preview`, service worker active) proxied to the API running with
`ASPNETCORE_ENVIRONMENT=Testing` (in-memory branch, seeded accounts, no live email path), driven
through a scripted 1v1 draft into the live position-draft stage.

## §14 measured performance

| §14 budget | Measured | Verdict |
|---|---|---|
| Initial load: core shell usable ≤ 3 s on typical 4G | **1.36 s** load event on emulated *Slow* 4G (1.6 Mbps / 150 ms RTT); **0.33 s** on typical 4G (9 Mbps / 60 ms RTT); ~205 KB compressed first-load transfer. Median of 3 cold loads (no SW, no HTTP cache), Chromium CDP throttling | ✅ |
| Standard authenticated reads < 500 ms p95 | `GET /drafts` p95 **1.7 ms**, `GET /me/notifications` p95 **1.8 ms**, `GET /players?take=50` p95 **2.6 ms**, `GET /users` p95 **1.6 ms** (n=120 each, local in-memory host — excludes WAN latency). Live Cloud Run reference: `/health` p50 **155 ms** TLS round-trip from this workstation | ✅ |
| Accepted pick visible to connected clients ≤ 500 ms p95 | REST-accept → SignalR `DraftUpdated` at a connected client: **p95 0.2 ms** (event effectively rides the same server tick); full request → visible p50 **2.6 ms**, p95 **277 ms** including one cold-path outlier (n=23 broadcast mutations, local host) | ✅ |

Bundle after route-level code splitting (PR-22): entry JS **96.0 KB gzip** + shared runtime
**22.6 KB gzip** (was 133.2 KB in one chunk), CSS 19.5 KB gzip; results/archive/explorer and all
seven admin modules are lazy chunks (1–4 KB gzip each). Service-worker precache: 46 entries,
1.20 MB (fonts included).

## Automated accessibility

- **Playwright + axe (`e2e/release-hardening.spec.ts`)** — real rendering incl. color-contrast,
  WCAG 2A/AA/2.1 AA/2.2 AA tag set: sign-in journey in **light and dark**, password recovery,
  plus 375 px and 844×390 landscape no-horizontal-scroll checks and a keyboard-operability walk.
- **vitest + axe-core (`src/test/a11y.test.tsx`)** — component-level scans of the authenticated
  core journeys: draft hub (install card), live draft room on the clock, profile
  (color-contrast + page-landmark rules excluded in jsdom; covered by the Playwright pass).
- Findings fixed during PR-22: `aria-label` on a generic `div` (BrandMark → `role="img"`), and
  12 px eyebrow labels using raw magenta at **3.6:1** — new `--color-secondary-text` AA ramp
  (**6.5:1** light `#b3126b`, **7.7:1** dark `#ff7ec0`).

## Manual review matrix (§17.9 done-when)

| Review | Evidence |
|---|---|
| Keyboard | e2e keyboard walk reaches email → password → show-password → submit; dialogs use the shared `Modal` focus trap (PR-18/21), confirm sheet keeps Escape/Tab behaviour with the new offline note |
| Reduced motion | Global kill-switch in `base.css` verified still active; `draft-room-reduced-motion.png` renders identical state without animation |
| Contrast (both themes) | axe color-contrast green in light + dark on rendered pages; token ramps documented above; `hub-375-dark.png`, `draft-room-375-dark.png` |
| 44×44 targets | Audit of all interactive CSS: only `.banner-dismiss` (36 px) failed → extended to 44 px hit area; `.invite-chip button` already carried a 44 px pseudo-element hit area |
| 375 px | `login-375-light.png`, `hub-375-light-install-card.png`, `draft-room-375-light.png`; zero horizontal overflow asserted in e2e |
| Landscape | `draft-room-landscape.png` (844×390): compact sticky room header keeps the clock visible, dialogs scroll (`max-height: calc(100dvh - 2rem)`) |
| Safe area | Topbar/sidebar/bottom-nav/action-bar/page-container all carry `env(safe-area-inset-*)` on every exposed edge; banners anchor above the home indicator |
| Offline / update states | `offline-banner-375.png` (real `context.setOffline`); pick confirmation disables with an explanation while offline; update prompt covered by vitest (banner, Refresh/Later, re-raise) |
| iOS install path | Profile + hub guidance with the Safari Share → Add to Home Screen steps (`profile-375-install-guidance.png`); Chromium `beforeinstallprompt` captured and offered in-product |

## Version handshake + caching proofs

- Backend: every `/api` response stamps `X-DraftRoom-Contract` + `Cache-Control: no-store`
  (integration-tested, including on 401s); anonymous `GET /api/meta/version` reports
  `{contract, revision}`; `/health` now includes `contract`, `revision`, and a `self` check on
  both storage branches.
- Frontend: compiled-in contract compared on every axios response and at boot/foreground via
  `/api/meta/version`; a mismatch nudges the service worker and raises the refresh prompt
  (`virtual:pwa-register` `onNeedRefresh` flow, hourly + on-visible update checks).
- Drift guard: `apiContract.test.ts` reads `ApiContract.cs` and fails CI if the two sides differ.
- Service worker: `navigateFallbackDenylist` for `/api`, `/hubs`, `/health`, `/swagger`; empty
  runtime caching; e2e proof parses the generated `dist/sw.js` (no API URL precached, denylist
  present, no runtime caching strategy exists).
