# PR-23 — MVP acceptance evidence (16 July 2026)

Verification record for PRD §17.9's done-when: **every §16 criterion linked to its automated
test(s) or recorded manual evidence**, the resilience proofs, the reproducible-build record, and
the §15 analytics instrumentation. Extends the [PR-22 evidence](PR22_EVIDENCE.md) pattern.

**Test baselines at PR-23:** `dotnet test FcDraft.sln` → **341** (222 unit, 77 hermetic
integration, 42 Testcontainers PostgreSQL — skip cleanly without Docker); `npm run test:run` →
**103**; `npm run test:e2e` → **14** client-only checks; `npm run test:e2e:full` → **5**
full-stack multi-client journeys (new). All paths below are repo-relative.

## How the full-stack E2E harness works

`fc-draft-web/playwright.fullstack.config.ts` boots the REAL stack: the API via
`dotnet run` in environment **Testing** (in-memory branch; seeded `mdevansh@gmail.com` +
`player@draftroom.dev` accounts plus the PR-23 demo players from `Database__SeedDemoAccounts`;
Brevo deliberately unconfigured so no email can leave the machine — never environment
Development) on `127.0.0.1:5089`, behind the production `vite preview` build proxying `/api` +
`/hubs` (the PR-22 seam, now parameterized by `DRAFT_API_ORIGIN`) on `:4174`. Suites in
`fc-draft-web/e2e-full/` drive real multi-client browser sessions; `helpers/api.ts` mirrors the
integration suite's REST sequence (create → invite → join → lock → teams → ready → start →
spinner → open-clubs → club-select → open-positions → pick) to fast-forward the resilience
scenarios. CI runs them in the dedicated `e2e-full` job (`.github/workflows/ci.yml`).

## §16 acceptance matrix — all 13 criteria

| § | Criterion | Automated evidence | Manual/recorded evidence |
|---|---|---|---|
| 16.1 | Admin creates an account with a temporary password, working invite email, forced change before entering the app *(the fixed `Draft@1234` was superseded by a unique one-time secret per invite — PR-05 decision, §5.1)* | `tests/FcDraft.Api.IntegrationTests/ForcedPasswordChangeTests.cs` (invite → captured one-time password → forced change → re-login; must-change token reaches only the change endpoint), `AuthenticationTests.cs`, `tests/FcDraft.UnitTests/IdentityServiceTests.cs` (`CreateUserAsync_invites_with_a_verifiable_one_time_password_and_forces_a_change`), vitest `LoginPage`/`RequireAuth` guard tests | Live-Brevo delivery + real forced change recorded per [PR23_DEVICE_SESSIONS.md](PR23_DEVICE_SESSIONS.md) step 1 |
| 16.2 | Accessible password eye controls; every visible control linked or explicitly `Coming soon` | vitest `PasswordField` coverage in the component suites; `e2e/release-hardening.spec.ts` keyboard walk incl. show-password; the full-stack journeys click every lobby/draft control by its accessible name (`e2e-full/helpers/ui.ts` — a dead or renamed control fails the run) | PR-22 manual review matrix ([PR22_EVIDENCE.md](PR22_EVIDENCE.md)); §12.4 disabled-`Coming soon` controls verified in PR-21/22 |
| 16.3 | 1v1 lobby 2–10 and 2v2 lobby 4–16; capacity + even-team rules enforced | `tests/FcDraft.UnitTests` capacity rules; `tests/FcDraft.Api.IntegrationTests/DraftLobbyTests.cs` (create/invite caps, under-minimum + odd-count lock rejection); `tests/FcDraft.Api.DatabaseTests/DraftLobbyDbTests.cs`; **end-to-end:** `e2e-full/1v1-draft.spec.ts` + `e2e-full/2v2-draft.spec.ts` run complete 2-client and 4-client lobbies | Device sessions A/B |
| 16.4 | 2v2 host assigns seeds and forms teams of exactly one Seed 1 + one Seed 2 | `tests/FcDraft.UnitTests` team-formation rules; `DraftTeamFormationTests.cs` / `DraftTeamFormationDbTests.cs`; **end-to-end:** `e2e-full/2v2-draft.spec.ts` assigns all four seeds and pairs both teams through the real seed toggles/pair builder, and proves non-hosts have no seed controls | Device session B |
| 16.5 | Only the host starts a ready lobby; the server commits a random spinner order for every formed team | `DraftTeamFormationTests.cs` (host-only + readiness-gated start), spinner idempotency/uniqueness tests (unit + `DraftTeamFormationDbTests.cs`), Fisher–Yates permutation test; **end-to-end:** both journey suites assert the guest never sees Begin-ready-check/Start/Spin controls and that every client renders the same committed order | Device sessions A/B step 4–5 |
| 16.6 | In spinner order, teams choose five-star clubs and protect one eligible player | `DraftClubAndPositionTests.cs` (straight order, club uniqueness, club-match + 75+ protect, position gate), `DraftClubAndPositionDbTests.cs` (unique indexes fire transactionally); **end-to-end:** both journey suites complete the club round through the real select → protect → confirm-sheet flow | Device sessions step 6 |
| 16.7 | Position draft starts ST → LW → RW, 120 s per team, only matching men's base players 75+ | `DraftTurnOrder` snake unit tests; `DraftTimerAndControlTests.cs` + `DraftTimerDbTests.cs` (restart-safe clock, deterministic auto-pick exactly-once under concurrency); eligibility unit/integration tests; **end-to-end:** journey suites assert the first slot on the clock is `ST`, the `role=timer` countdown renders on every client, and 30 position picks complete the snake | Device sessions step 7 + the deliberate expiry drill (step 9) |
| 16.8 | Cards expose stats, alt positions, `+`/`++` roles, PlayStyles; excluded content never enters the pool | `GET /drafts/{id}/footballers/{id}` card tests (integration) and the pinned-catalog reads; `tests/FcDraft.Api.DatabaseTests/PlayerExplorerDbTests.cs` + dataset import tests (query boundary: <75, non-Kick-Off, non-active-version content never appears); vitest `PlayerDetailSheet`/room component tests | Device sessions step 7 (card inspection on device) |
| 16.9 | Every connected client sees presence, seeds, teams, spinner, clubs, picks, timer, squads in real time | `DraftLiveSyncTests.cs` (`Both_clients_receive_an_accepted_mutation_within_the_propagation_target` — §14 500 ms); **end-to-end:** the journey suites assert cross-client visibility at every stage over real websockets (presence flip, formed teams ×4 clients, committed order, pick progress advancing on the waiting client) with zero reloads | PR-22 measured propagation p95 0.2 ms after accept ([PR22_EVIDENCE.md](PR22_EVIDENCE.md)); device sessions throughout |
| 16.10 | Server prevents invalid capacity/team formation, duplicate/ineligible footballers, out-of-turn picks, stale commands | Guard unit tests; integration rejection tests (out-of-turn/duplicate/ineligible/wrong-state/stale); `DraftClubAndPositionDbTests.cs` + `DraftAggregateDbTests.cs` (unique indexes + version token under real races); **end-to-end:** `e2e-full/resilience.spec.ts` — two teammates confirm different players simultaneously through real clients; exactly ONE pick lands, the loser gets the §6.5 explanation and resyncs | — |
| 16.11 | A disconnected participant reconnects to authoritative state; host/admin recovery actions have a complete audit trail | `DraftLiveSyncTests.cs` (`A_reconnecting_client_gets_the_authoritative_snapshot_without_duplicating_actions`); `AdminDraftOperationsTests.cs` + `AnnouncementAndAuditDbTests.cs` (compensating recovery, byte-identical prior events, attributable actions); **end-to-end:** `e2e-full/resilience.spec.ts` — a real client goes offline mid-draft, four picks land without it, it reconnects (§7.4) showing all of them and continues with exactly one new pick | Device sessions step 10 (airplane-mode drill) |
| 16.12 | Installable PWA; core journeys at 375 px pass keyboard/reduced-motion/contrast/touch-target checks | `e2e/smoke.spec.ts` (manifest) + `e2e/release-hardening.spec.ts` (axe both themes, 375 px + landscape no-scroll, keyboard, SW-never-caches-API); vitest axe component scans + PWA lifecycle tests | PR-22 measured evidence + screenshots ([PR22_EVIDENCE.md](PR22_EVIDENCE.md)); real-iPhone install + standalone launch per device sessions step 13 |
| 16.13 | Brevo handles invitations, resets, draft invitations, and announcements through a reliable outbox | `tests/FcDraft.Api.DatabaseTests/EmailOutboxDbTests.cs` (commit-during-outage → backoff retry → delivery → secret cleared — the "outbox drains later" proof), PR-20/21 outage tests (a failing send never fails the mutation; throttled 20/15 s windows); **end-to-end:** `e2e-full/resilience.spec.ts` — with email down (Testing has no Brevo), a host cancels mid-draft through the UI: the mutation commits, the other client sees it live, in-app notices land, and the admin outbox view reports the FAILED attempts | Live-Brevo invite delivery recorded in device sessions / beta checklist §3 |

Nothing in §16 is unlinked. The two §16 phrasings superseded by recorded product decisions —
the fixed `Draft@1234` temporary password (→ unique one-time secret, PR-05, §5.1) — are noted
inline in the matrix and in the PRD.

## Resilience & race evidence (PRD §17.9 scope item 2)

| Scenario | End-to-end proof (real clients) | Underlying server proof |
|---|---|---|
| Simultaneous 2v2 teammate submissions | `e2e-full/resilience.spec.ts` — both teammates stage different players and confirm at the same instant; exactly one pick recorded, first valid wins, loser explains + resyncs | Version token + unique `(draft, footballer)`/`(team, slot)` indexes: `DraftClubAndPositionDbTests.cs`, concurrent-expiry exactly-once: `DraftTimerDbTests.cs` |
| Stale-version 409 recovery | Same spec — the losing client's alert + automatic resync to the authoritative snapshot | `A_stale_command_conflicts_and_the_client_refreshes_cleanly` (`DraftLiveSyncTests.cs`) |
| Reconnect mid-draft | Same spec — offline banner, draft advances 4 picks, reconnect shows all with no duplicates, play continues | Reconnect-snapshot integration test; persisted turn anchor (PR-16) |
| Brevo outage | Same spec — cancellation commits + propagates live; failed sends visible to the admin | Durable-outbox retry/drain: `EmailOutboxDbTests.cs`; enqueue-inside-transaction: PR-20/21 tests |

## §15 analytics instrumentation (scope item 5)

Vendor-neutral seam `IProductAnalytics` (mirrors PR-22's `IOperationalMetrics`; default
`DraftRoomAnalytics` publishes on the same `FcDraft.DraftRoom` meter — exportable via any OTel
listener, no vendor SDK anywhere). Instruments and the §15 metric each supports:

| Instrument | §15 metric |
|---|---|
| `draftroom.users.invited` / `draftroom.users.activated` | Invite-to-activation conversion |
| `draftroom.drafts.created` / `draftroom.drafts.started` `{format}` | Lobby-to-draft-start conversion |
| `draftroom.drafts.ended` `{format, outcome}` | Draft completion rate |
| `draftroom.drafts.time_to_first_pick` (s) | Median time from lobby creation to first pick |
| `draftroom.picks.accepted` `{format, auto}` + `draftroom.picks.turn_duration` (s) | Median pick time and timer-expiry rate |
| `draftroom.hub.joins` `{reconnect}` (new `RejoinDraft` hub method; the client's reconnect handler uses it) | Reconnection success |
| `draftroom.drafts.interventions` `{action, actor}` | % of drafts requiring admin intervention |
| `draftroom.email.delivery` `{outcome}` (both delivery branches) | Transactional email delivery rate |

Privacy (§15 "must not capture"): every method accepts only low-cardinality facts — format,
outcome, action, durations. `tests/FcDraft.UnitTests/ProductAnalyticsTests.cs` proves each
instrument fires with exactly the whitelisted tag keys (`format/outcome/auto/action/actor/
reconnect`) — no ids, emails, passwords, tokens, or content can reach a measurement — and that
only the FORCED first password change counts as activation. Recorded as **not server-measurable
and deliberately deferred**: PWA installation rate and email click-through (client-only /
Brevo-side; monthly-active and repeat-drafter counts derive from the database directly).

## Reproducible build (scope item 4 / done-when)

Proven two ways:

1. **Continuously in CI** — every run builds the production container from a clean checkout
   (`container` job) and boots the API from source for the full-stack E2E job; the deploy uses
   the same `Dockerfile` via Cloud Build.
2. **Fresh local clone (16 July 2026, this commit):** `git clone` → `dotnet test FcDraft.sln`
   → **341/341 pass** (incl. all 42 Testcontainers PostgreSQL tests against a throwaway
   container) → `npm ci && npm run test:run` → **103/103** → `npm run build` green →
   `docker build .` succeeds and the image serves `GET /health` → 200
   (`{"status":"healthy","contract":1,…}`) and the SPA shell → 200 with only `PORT` injected
   (in-memory branch, no secrets).

## Seed/demo data (scope item 4)

- `Database:SeedDemoAccounts` (default **false**, both storage branches) seeds three additional
  deterministic demo players (`player2/3/4@draftroom.dev` — `src/FcDraft.Infrastructure/Auth/DemoAccounts.cs`),
  giving the 4+ activated accounts a 2v2 lobby needs without live email. Proven by
  `IdentityServiceTests.Demo_accounts_seed_only_when_the_flag_is_enabled` and used by the
  full-stack harness; never enabled in production (RUNBOOK §3, beta checklist §1).
- `scripts/seed-demo-lobby.mjs` REST-seeds a ready beta lobby for device sessions.

## Manual/device evidence

The physical-device sessions the done-when requires are run by the operator using
[PR23_DEVICE_SESSIONS.md](PR23_DEVICE_SESSIONS.md) (session matrix, 13-step checklist, capture
template); completed records append there and screenshots land in `assets/pr23-evidence/`. The
release gate itself is [../../PRIVATE_BETA_CHECKLIST.md](../../PRIVATE_BETA_CHECKLIST.md).
