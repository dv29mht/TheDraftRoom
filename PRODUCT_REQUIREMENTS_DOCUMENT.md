# The Draft Room — Product Requirements Document

**Document status:** Draft v0.25 (PR-23 complete — MVP verified)  
**Date:** 16 July 2026  
**Product owner:** TBD  
**Platforms:** Responsive web and installable Progressive Web App (PWA)  
**Supported formats:** 1v1 and 2v2  
**Player dataset:** EA SPORTS FC 26 men's footballers only

**v0.2 update:** Reframed drafts as multi-team tournament lobbies; added capacity limits, host-controlled 2v2 seeding, spinner ranking, five-star club/protected-player selection, 75+ player eligibility, detailed Kick Off player data, forced temporary-password change, functional scaffold linkage, and 120-second real-time turns.

**v0.3 update:** Established UI UX Pro Max as the required design-intelligence skill for UI/UX design, implementation, and review, with project-level design-system persistence for continuity across sessions.

**v0.4 update:** Adopted the supplied asset moodboard as the canonical visual reference; replaced the earlier lime-led palette with the approved neon-pink/magenta/violet broadcast identity and persisted the master design system.

**v0.5 update:** Renamed the product to **The Draft Room** across the product requirements, application identity, and persisted design-system memory. The existing moodboard remains the visual reference; any legacy name shown inside that reference asset is superseded by this product name.

**v0.6 update:** Made the application light-first with a persistent dark-mode option, refined cards and typography, replaced the Player Explorer placeholder with an interactive module, and connected Admin Users to account creation and invitation APIs.

**v0.7 update:** Added a real-time admin activity centre for player sign-ins and newly created draft rooms, made draft-room creation functional, and established server-backed user-directory pagination with 10, 25, and 50-row views.

**v0.8 update:** Replaced the broad delivery phases with a dependency-ordered, one-session-per-PR implementation roadmap. Added a checked baseline, PR scope boundaries, definition-of-done rules, verification requirements, and a progress-tracking convention for future sessions.

**v0.9 update (14 July 2026):** Interim foundation work ahead of PR-01. Completed in-memory user-management CRUD by adding an authoritative update/edit path (`PUT /api/users/{id}`, with validation and a self-demotion guard) and an accessible admin edit dialog to the existing create/read/delete actions. Enabled live Brevo invitation delivery and removed a stale seeded-player reference from the README. Recorded a security follow-up: the Brevo API secret currently lives in committed `appsettings.json` and must move to environment or gitignored configuration before source control (see PR-00 known limitations and PR-05/PR-06). No numbered roadmap PR is complete; the next session remains **PR-01**.

**v0.10 update (14 July 2026):** Interim security hygiene ahead of PR-01. Relocated the live Brevo API key out of committed `appsettings.json` — the committed file now leaves `Brevo:ApiKey`/`Brevo:SenderEmail` blank (reporting `emailConfigured: false` on a fresh clone), the real secret moves to gitignored `appsettings.Development.json` (or `Brevo__*` environment variables), and a committed `appsettings.Development.json.example` documents the shape. Verified the API build stays green and that `GET /api/admin/settings` still reports `emailConfigured: true` under the Development configuration layer. The pre-source-control secret follow-up in the PR-00 baseline is now resolved. No numbered roadmap PR is complete; the next session remains **PR-01**.

**v0.11 update (14 July 2026):** Interim account-lifecycle work ahead of PR-01, and a down payment on PR-04. Enforced the `AccountStatus` enum end-to-end in the in-memory foundation: added `SetUserStatusAsync` to the identity service and admin `POST /api/users/{id}/activate` and `/deactivate` endpoints (administrator accounts are protected from deactivation), wired an activate/deactivate control and a three-state Deactivated/Pending/Active status column into the Admin Users directory, and rejected deactivated users both at sign-in (`403`) and when creating/joining draft rooms (`403`, which also stops a token issued before deactivation). Added a seeded Development player so the lifecycle can be exercised locally without sending a real invitation. Verified with both builds green and a scripted API drive of deactivate → login `403` → room-create `403` → reactivate → login `200`, plus admin-protection `400` and not-found `404`. This does **not** complete PR-04, which still requires SQL persistence, database-side pagination, historical retention, and removal of the hard-delete action; hard delete remains for now. No numbered roadmap PR is complete; the next session remains **PR-01**.

**PR-01 completed (14 July 2026):** Locked all twelve MVP draft-rule decisions in [`DRAFT_RULES.md`](DRAFT_RULES.md) — 16-player squad (1 held + 11-player 4-3-3 XI + 4 flexible subs), snake round order, global footballer and per-lobby club uniqueness, either-teammate 2v2 pick authority, best-available auto-pick on timer expiry, open host permission, and the EA-feed-plus-secondary-roles data source with media deferred. Reconciled §5, §6.3/§6.4, §19, and §20; the next session is **PR-02**.

**PR-03 completed (15 July 2026):** Added the database persistence foundation on **PostgreSQL** (EF Core). **Engine decision:** the roadmap originally named SQL Server; PR-03 adopts PostgreSQL instead because the target hosting platform offers managed PostgreSQL and it runs natively on the development machine (Apple Silicon) without emulation. §12 and the PR-03/PR-04 scope wording are updated accordingly per §17.10.4; the persistence design (EF Core, explicit snake-case mappings, migration-created schema, health check, transaction abstraction) is unchanged. Delivered: an EF Core `FcDraftDbContext` with explicit snake-case table/column mappings and a unique normalized-email index; an `InitialCreate` migration (the schema is created exclusively from migrations — no `EnsureCreated`, no manual DDL); a startup `IDatabaseInitializer` that applies pending migrations and idempotently seeds platform metadata and the deterministic development accounts; an `EfIdentityService` behind the existing `IIdentityService`; an `ITransactionRunner` transaction abstraction; a `database` health check wired into `/health`; and `users` + `platform_metadata` tables. Persistence is **opt-in by connection string** — with `ConnectionStrings:DraftRoom` blank the app keeps the in-memory foundation, so a fresh clone and the hermetic suite need no database; supplying it switches the identity store onto EF Core. No secret is committed (connection string lives in gitignored `appsettings.Development.json` or `ConnectionStrings__DraftRoom`; the committed `appsettings.json` is blank; the example documents the shape). Tests: a new `tests/FcDraft.Api.DatabaseTests` boots the real API against a throwaway PostgreSQL container (Testcontainers) and proves migration-created schema, user/password persistence across a simulated restart, `/health` database reporting, and transaction commit/rollback — skipping cleanly when Docker is absent and running for real in CI; a Docker-free unit test covers the unhealthy health-check path. Verified: `dotnet test FcDraft.sln` → 51 passing (29 unit, 16 hermetic integration, 6 PostgreSQL persistence) with the container running; `npm run test:run` → 14 passing; both production builds green; in-memory `/health` returns 200 with an empty check set and seeded-admin login returns 200. The next session is **PR-04**.

**PR-02 completed (14 July 2026):** Added the automated-test and CI foundation. Introduced a `FcDraft.sln` and two .NET test projects — `tests/FcDraft.UnitTests` (validators, the login/change-password handlers, and the in-memory identity service: invite, deactivation, password verification/rotation) and `tests/FcDraft.Api.IntegrationTests` (a `WebApplicationFactory` that boots the real API with a fake Brevo sender and covers login, the full invite → forced password change → re-login flow, protected-route `401`/admin `403` authorization boundaries, deactivation enforcement including a pre-deactivation token, and draft-room creation). Added a Vitest + Testing Library component suite (route guards, the login flow and navigation linkage, API error mapping and the auth header interceptor) and a Playwright PWA smoke scaffold (login render, anonymous → `/login` redirect, manifest served). Added a three-job GitHub Actions workflow (backend restore/build/test, frontend `npm ci`/Vitest/build, Playwright e2e). All suites are deterministic and never call live Brevo or any external FC service — the fake sender captures the one-time password to drive the invite flow. Verified: `dotnet test FcDraft.sln -c Release` (45 passing), `npm run test:run` (14 passing), `npm run test:e2e` (3 passing), and both production builds green. The next session is **PR-03**.

**v0.16 update (15 July 2026) — PR-04 through PR-09 delivered in one session:**

- **PR-04 completed:** Durable user directory — DB-side search/paging/tallies (never loads the whole directory), historical retention with the hard-delete action removed (deactivate-and-retain only), optional avatar/preferred-team-name profile fields, and by-id lookups replacing full scans. Migration `AddUserProfileFields`.
- **PR-05 completed:** Authentication security & session revocation — **the `Draft@1234`-vs-unique-secret decision (§5.1) is resolved in favour of a unique one-time secret per invite** (more secure; already issued by the foundation). Adopted BCrypt hashing (PRD §12.3) with transparent legacy-hash verification; server-side forced-password-change enforcement (a must-change token reaches only `/api/auth/change-password`); a security-stamp embedded in every token and re-checked per request so password change/reset, deactivation, admin action, and sign-out-everywhere revoke older tokens immediately; failed-login rate limit + temporary lockout; forgot/reset-password tokens (SHA-256-hashed, single-use); logout-all; and an append-only security-audit trail. Migration `AddAuthSecurity`.
- **PR-06 completed:** Durable Brevo email outbox — account transactions commit even during a Brevo outage; a background worker delivers with exponential-backoff retry, clears the secret after send, and exposes delivery status to admins without leaking the secret. In-memory mode keeps inline delivery. Migration `AddEmailOutbox`.
- **PR-07 completed:** Versioned footballer & club import — dataset versions, footballers (positions normalized for filtering; stats/roles/PlayStyles as jsonb), clubs, and per-row import issues; validate → import as draft → activate (archives the previous active, retains history); errors block activation. Bundled FC 26 dataset seeds a fresh DB. Club five-star ratings are absent from the source feed and curated in PR-09. Migration `AddPlayerDataset`.
- **PR-08 completed:** Server-backed Player Explorer — `/api/players` paged search (prefix/substring), position/rating/club/league/nation filters, and name/overall sort over the **active** dataset; the UI is migrated off the static JSON; query-boundary tests prove excluded/inactive (<75, non-Kick-Off, non-active-version) content never appears.
- **PR-09 completed:** Roster templates & eligible clubs — versioned ordered templates with slot rules and the 120s timer, active/inactive state, and the locked 4-3-3 default seeded; admin curation of eligible five-star Kick Off clubs from the active dataset. Templates are the snapshot source a draft freezes at start (PR-10). Migration `AddRosterTemplates`.

**Engine decisions in this session:** password hashing is **BCrypt** (§12.3); the temporary-credential scheme is a **unique one-time secret** (§5.1, §18). **Verification:** `dotnet test FcDraft.sln` → 92 passing (40 unit, 32 hermetic integration, 20 PostgreSQL persistence via Testcontainers); `npm run test:run` → 14 passing; both production builds green; a running-process smoke drove login, the explorer, roster template, dataset, forgot-password, and forced-change endpoints. The next session is **PR-10 — Persistent draft aggregate and append-only event history**.

**PR-10 completed (15 July 2026):** Built the authoritative, durable draft lifecycle foundation. Added the `Draft` aggregate and its `DraftParticipant`, `DraftTeam`, `DraftTeamMember`, `DraftRosterSlot`, and append-only `DraftEvent` entities; a pure `DraftStateMachine` enforcing exactly the §10.1 transition table; and `DraftStateProjection.Replay`, which rebuilds/verifies the current status and version from event history. Every accepted transition bumps a `Draft.Version` and appends one immutable `DraftEvent` (sequence-numbered, unique per draft) — so the event stream is both the audit trail (§9.10) and the source of truth. Optimistic concurrency is enforced two ways (§6.5): a last-seen `ExpectedVersion` on every command (mismatch → `ConflictAppException`/409) and the `version` column mapped as an EF concurrency token, with the append-only `(draft_id, sequence)` unique index guaranteeing only one transition wins a race. Starting a draft (the `ReadyCheck → SpinnerRanking` move) **snapshots configuration** (§9.4): it copies the active roster template's ordered slots into `DraftRosterSlot`, snapshots the pick timer, and pins the active `PlayerDatasetVersion`, so later template/dataset edits cannot mutate an in-progress draft. Audited MediatR command handlers (`CreateDraft`, `TransitionDraft`, `StartDraft`) plus `GetDraft`/`ListDrafts` queries wrap read → validate → mutate → append in the existing `ITransactionRunner`, so an invalid transition or version conflict leaves **no partial write**. Persistence is opt-in as usual: `IDraftStore` has an `EfDraftStore` (SQL) and an `InMemoryDraftStore` implementation, and the no-database branch also registers an `InMemoryTransactionRunner`; the legacy in-memory draft-room stub is superseded and folds into the real lobby in PR-11. One event beyond the PRD §10.2 list was added — `DraftAbandoned` — to record the §10.1 `→ Abandoned` terminal transition. This is a backend-foundation PR: no new HTTP endpoints or UI (the lobby/create/start/pick surfaces are PR-11–PR-18). Migration `AddDraftAggregate` (6 tables, created exclusively from migrations). **Verification:** `dotnet test FcDraft.sln` → 161 passing (102 unit, 34 hermetic integration, 25 PostgreSQL via Testcontainers) — new tests prove the §10.1 allowed/denied matrix, create/transition/start handlers, stale-version → 409 with no partial write, illegal transition rejected with no partial write, the `version` token blocking a lost update under a real race, and state rebuilt from history; frontend untouched. The next session is **PR-11 — Lobby creation, invitations, and attendance**.

**PR-11 completed (15 July 2026):** Turned the PR-10 foundation into a usable 1v1/2v2 lobby (§9.4, §7.2–7.3). `CreateDraftCommand` now creates the lobby, adds the creator as a joined **host** `DraftParticipant`, opens it (Draft → Lobby), binds a host-chosen roster template (falling back to the active one), and seeds an initial invite list — each invitee validated (active account, within capacity) and recorded with a `ParticipantInvited` event, all in one transaction. New audited, version-checked MediatR commands drive attendance: `InviteParticipant`, `JoinDraft` (self-service presence, idempotent), host-only `RemoveParticipant`, and host-only `LockLobby` (Lobby → TeamFormation) — each appends exactly one `DraftEvent`. **Server-side capacity is enforced** (§6.2): the format maximum is checked on create/invite (1v1 ≤ 10, 2v2 ≤ 16) and the minimum + even-count are enforced at lock (1v1 2–10, 2v2 4–16 even); deactivated accounts are rejected at host/invite/join. A new authenticated `DraftsController` exposes create, the authoritative lobby snapshot (`GET`, restricted to participants/host/admin so a lobby's existence is not leaked to outsiders), a caller-scoped draft list, an invitable-users directory, roster templates, and invite/join/remove/lock. `GetDraft` now enriches participants with display name/email; `DraftDetail` carries a `LobbyCapacity` block so the UI renders the rules the server enforces. `StartDraft` now snapshots the draft's **bound** template (not whatever is active at start). The legacy `/api/draft-rooms` stub, `IDraftRoomService`, and `DraftRoom` type were **retired** and the frontend migrated off them. Two events beyond §10.2 were added — `ParticipantRemoved` and `LobbyLocked` — for the §10.1 actions §10.2 did not name; both persist as strings, so **no migration was required** (the PR-10 `DraftParticipant`/`DraftEvent` tables are simply now populated). Frontend: a rebuilt **New lobby** flow (format, template picker, invite search), a **lobby detail** route (`/drafts/:id`) showing per-slot invite/join state, host controls (invite/remove/lock), and a self-service "Confirm presence" for invitees, plus a per-user **draft hub** list; Start-draft/seeding stay disabled and labelled coming soon (PR-12/PR-13). **Verification:** `dotnet test FcDraft.sln` → 190 passing (124 unit, 38 hermetic integration, 28 PostgreSQL via Testcontainers) — new tests prove capacity enforcement (under/over/odd) server-side, deactivated-user rejection, host-only remove/lock, join presence, and reopening the authoritative snapshot; `npm run test:run` → 21 passing (new lobby-create and lobby-detail component tests); both production builds green; a running-process smoke drove login → create lobby → capacity-gated lock (400) → invite → deactivated-reject (400) → valid lock (→ TeamFormation) → scoped list. The next session is **PR-12 — 2v2 seeding, team formation, and ready check**.

**PR-12 & PR-13 completed (15 July 2026):** Made both formats start-ready and added the server-authoritative spinner (§6.2, §9.4–§9.5, §7.3). **Team formation (PR-12):** host-only `AssignSeed` sets a participant's Seed 1/Seed 2 (2v2 only, team-formation state only); `FormTeams` replaces the team layout — 1v1 auto-projects one solo team per participant, 2v2 pairs participants into teams each **exactly one Seed 1 + one Seed 2** with no participant on more than one team; self-service `SetReady` toggles readiness; and host-only `BeginReadyCheck` (TeamFormation → ReadyCheck) / `ReopenTeamFormation` (ReadyCheck → TeamFormation, clearing readiness so revised teams are re-confirmed) drive the ready check. Re-forming teams clears readiness. **`StartDraft` is now gated** by the §9.4 rule — all present, everyone assigned to a valid team (2v2 seed pairing), and everyone ready — so it can no longer start an unformed/unready draft; the same `DraftFormation.Evaluate` powers a new `StartRequirements` block on `DraftDetail` so the client's Start/ready-check controls render exactly the rules the server enforces. Each change bumps `Draft.Version` and appends one `DraftEvent`. **Spinner (PR-13):** host-only `CommitSpinner` (SpinnerRanking state) draws an unbiased **Fisher–Yates** order through an injected `IShuffler` seam (mirrors the `TimeProvider` pattern — `Random.Shared` in production, a deterministic shuffler in tests), assigns each team one unique `SpinnerRank`, and appends `SpinnerOrderCommitted` + `SpinnerOrderRevealed`; it is **idempotent** — a committed order is never reshuffled by a retry, and the animated wheel only reveals the server result. The `DraftsController` gains `seeds`, `teams`, `ready`, `ready-check`, `reopen-teams`, `start`, and `spinner` endpoints. Two events beyond §10.2 were added — `ReadyCheckStarted` and `TeamFormationReopened` — for the §10.1 transitions §10.2 did not name; both persist as strings, so **no migration was required** (the PR-10 seed/ready/team columns and the `(draft_id, spinner_rank)` and `(draft_id, participant_id)` unique indexes already exist). Frontend: the lobby detail route grows a team-formation stage (2v2 seed toggles + a pair builder, 1v1 solo-team confirm, a validation summary), a ready-check stage (ready toggle, reopen-teams, gated **Start draft**), and a spinner stage — an animated wheel with a reduced-motion-safe committed-order list and a host **Spin** control. Club selection stays "Coming soon" (PR-14). **Verification:** `dotnet test FcDraft.sln` → 216 passing (145 unit, 41 hermetic integration, 30 PostgreSQL via Testcontainers) — new tests prove seed validity, the one-Seed 1 + one-Seed 2 team rule, host-only formation, the Start gate (rejects unassigned/unready/invalid teams), and spinner rank uniqueness + idempotency + determinism under an injected shuffle + non-host rejection; `npm run test:run` → 27 passing (team-formation, ready, and spinner component tests); both production builds green. The next session is **PR-14 — Five-star club and protected-player round**.

**PR-14 & PR-15 completed (16 July 2026):** Built the pre-draft club/held round and the position-draft pick engine (§9.5–§9.6, §6.3, DRAFT_RULES §1–5) — the **first schema change since PR-10**. **Turn order** is a pure Domain function `DraftTurnOrder` (like `DraftStateMachine`): straight ascending rank for the club/held round, **snake** for the position rounds (round `r` ascending when odd, descending when even), reusable by PR-16's auto-pick. **PR-14 (five-star club + protected player):** host-only `OpenClubSelection` (SpinnerRanking → ClubSelection, new string event `ClubSelectionStarted`); `SelectClubAndProtect` — in **straight spinner order**, the active team (either teammate, or an admin) picks one five-star club (unique per lobby) and protects one 75+ Kick Off footballer **from that club**, appending `ClubSelected` + `FootballerProtected` at one version and adding the held pick (slot Order 0); host-only `OpenPositionDraft` (ClubSelection → PositionDraft, `PositionRoundStarted`) is gated until **every** team has locked its club and protected player. **PR-15 (position draft):** host-agnostic `SubmitPick` accepts the **first valid** teammate submission for the active (team, slot) — derived by snake order over the frozen `DraftRosterSlot` snapshot (ST → LW → RW → CM×3 → LB → CB×2 → RB → GK, then 4 flexible subs) — enforcing turn, position eligibility (primary/alt match; a flex bench slot accepts any), 75+ rating, pinned-dataset membership, and availability, appending one `PickAccepted`; filling the final slot transitions **PositionDraft → Completed** (`DraftCompleted`). A new `IDraftCatalog` read seam scopes every club/footballer read to `Draft.PinnedDatasetVersionId` (never the active dataset), backed by `EfDraftCatalog` (DB) and `InMemoryDraftCatalog` (bundled snapshot); a curated default five-star club set is seeded at dataset import so the round works out of the box. **Schema/migration `AddDraftPicks`:** a shared `draft_picks` table (held + every position pick) with unique `(draft_id, footballer_id)` (global footballer uniqueness) and `(draft_team_id, slot_order)` (each slot fills once), plus a unique `(draft_id, selected_club_id)` on `draft_teams` (club uniqueness) — all NULL-distinct/forward-safe, applied on the prod deploy. The `DraftsController` gains `open-clubs`, `club-select`, `open-positions`, `pick`, and a `board` read endpoint (whose turn + the eligible clubs/players for the current step). Frontend: the spinner "Coming soon" banner is replaced by a host **Open club selection** control, and the lobby route grows club-selection, position-draft, and completed stages (poll-based, driven by the server board); `lobbyStatusLabel` covers ClubSelection/PositionDraft/Completed. **Verification:** `dotnet test FcDraft.sln` → 251 (174 unit, 44 hermetic integration, 33 PostgreSQL via Testcontainers) — new tests prove straight club order, snake position order, club + footballer uniqueness (unique indexes fire transactionally), the protected-player club-match + 75+ rule, out-of-turn/stale/ineligible/duplicate/wrong-state rejection, 2v2 either-teammate authority, and completion → Completed; `npm run test:run` → 32 (club-selection + position-draft component tests); both production builds green. The next session is **PR-16 — Server timer and host controls**.

**PR-16 & PR-17 completed (16 July 2026):** Made time and exceptional states authoritative, then synchronized every client live (§6.4–§6.5, §9.6–§9.7, §17.7, DRAFT_RULES decision 7). **PR-16 (server timer + host controls):** the 120s pick clock is **persisted state, never an in-process countdown** — nullable `turn_started_at`/`paused_at` columns (migration `AddDraftTimer`, forward-safe) anchor the active position turn, so a refreshed client or restarted server computes the SAME remaining time; the club/held round is untimed (§6.4 times "each position"). Expiry is evaluated **lazily** in the read/command paths (board/get/pick/pause apply overdue auto-picks transactionally first) plus a belt-and-braces hosted sweep while the single instance is warm — scale-to-zero safe. On expiry the server auto-picks the deterministic §6.4 best (highest overall → name → id — the catalog's existing ordering) through the SAME shared `PickEngine` validation path as a human pick, recorded as a new string event `PickAutoSelected` (null actor, audited reason; no migration), advancing the turn anchored at the expired deadline (cascaded catch-ups stay exact) and completing the draft on the final slot; the version token + unique indexes guarantee concurrent expiry triggers yield exactly ONE pick. A `DraftTimerDto` (deadline/remaining/paused/15s-warning) rides `DraftDetail` and the board. Host controls: pause (ClubSelection/PositionDraft → Paused; clock freezes — paused time never elapses), resume (back to the state it paused from; anchor shifted by the pause), and cancel — host-or-admin with a required, length-capped audited reason; **admin-only** recovery (§9.7) appends a compensating `AdminRecoveryApplied` event with optional turn-clock restore and never edits history. **PR-17 (SignalR synchronization):** an authenticated `/hubs/draft` hub (JWT via `access_token` query string through `OnMessageReceived`; one group per draft; joining authorized exactly like `GET /drafts/{id}` with 404-equivalent rejection) receives a version-stamped `DraftUpdated` envelope `{draftId, version, eventType, detail}` after every accepted mutation — published post-commit by a new `IDraftNotifier` seam (no-op default in Application; SignalR-backed in the API via a MediatR post-handler behavior) — including timer auto-picks and pause/resume/cancel; a null `detail` (summary-only commands) means "refetch". Reconnect: `withAutomaticReconnect`, rejoin, and reconcile from the authoritative snapshot the join returns (versions only move forward client-side; REST reads remain the fallback — SignalR augments, never replaces). Single-instance Cloud Run keeps SignalR in-process (no backplane; 15s keep-alives). Frontend: `@microsoft/signalr`, a connection-status banner (connecting/reconnecting/offline), live snapshot application, a server-calibrated countdown with the 15s warning state, host pause/cancel controls with required reason, and paused/cancelled stages; the rich room UX stays PR-18. **Verification:** `dotnet test FcDraft.sln` → 276 (188 unit, 52 hermetic integration, 36 PostgreSQL) — new tests prove restart-safe remaining time, warning threshold, deterministic auto-pick exactly-once (including two CONCURRENT triggers against real PostgreSQL), cascaded cold catch-up, pause-freeze/resume-shift, host-only + reason-required controls, admin-only recovery, hub group fan-out to two clients **within the §14 500 ms propagation target**, unauthorized-join rejection, and reconnect reconciliation without duplicated actions; `npm run test:run` → 42 (countdown, warning, pause/resume/cancel, connection status, live-push tests); both production builds green. The next session is **PR-18 — Live draft room experience**.

**PR-18, PR-19 & PR-20 completed (16 July 2026):** Delivered the complete drafting experience, the immutable results archive, and participant communications. **PR-18 (live draft room):** the room's header shows active team/slot, round + pick progress, an explicit "Your turn" state, the server-calibrated countdown, and the live-connection indicator; eligible-player **search runs on the server inside the PINNED board** (`GET /drafts/{id}/board?search=&take=`, take deliberately raised to a 500 cap — never the whole pool, never `/api/players`); §9.6 player detail cards read on demand from a new `GET /drafts/{id}/footballers/{id}` (stats, roles with `+`/`++`, PlayStyles, club/league/nation via a new `CatalogFootballerCard` on the pinned catalog) and explain unavailability (who holds the player, in which slot); **no one-tap picks** — a confirmation sheet names the draft team and roster slot (protect goes through the same sheet); a personal shortlist (decision 11 — recorded as client-side localStorage per user AND per draft; the server never trusts it) marks since-taken/ineligible entries from server state; a compact team rail offers focused-squad, recent-picks, and full-history views (chronology re-derived from the deterministic snake rules, display-only); on iPhone the room replaces the global bottom nav with its own sticky action area (§8.3), with 44×44 targets, keyboard-operable dialogs, reduced-motion equivalents, and a clearer auto-resyncing 409 message. `LobbyPage` was decomposed into per-stage components (`components/draft/`). **PR-19 (results + archive):** `GET /drafts/{id}/results` serves a COMPLETED draft's per-team average + GK/DEF/MID/FWD line ratings (from the frozen slot positions), represented clubs/leagues/nations, member names, and the pick sequence **re-derived from the deterministic turn rules** (the exact acceptance order — recorded decision over `CreatedAt`, whose sub-second ties are less deterministic); **§9.7 immutability is satisfied WITHOUT a new snapshot table** (recorded per §17.10.4): ratings/identity live on the already-frozen `draft_picks`/`draft_roster_slots` (PR-15), display extras resolve from the pinned dataset version (immutable after import) via a bulk facts read, and the team's club name prefers the held pick's immutable facts because the five-star flag is admin-mutable. Frontend: a read-only `/drafts/{id}/results` page with FORMATION (pitch from frozen slot positions) and LIST views, rating chips, and the numbered sequence; the Draft Hub groups Live/Upcoming/Completed/Ended; the `/teams` placeholder became the squad archive. **PR-20 (notifications + draft emails):** a persistent `user_notifications` table (forward-safe migration `AddUserNotificationsAndEmailPreferences`, also adding `users.optional_email_opt_out` and a non-secret `email_outbox.payload`); a `DraftParticipantNotifier` appends notification rows + outbox emails **inside the mutating transaction** (invited, cancelled-with-reason, completed — including expiry-sweep completions — and a host-initiated reminder, the recorded MVP reminder trigger); new authorization-scoped `/api/me/notifications` list/mark-read/mark-all endpoints (someone else's id is a 404; the admin-only `/api/notifications` SSE centre is untouched) and a shell notification centre with unread badge and draft deep links; four new string-stored `EmailKind`s dispatch through the EXISTING outbox to a `BrevoDraftEmailSender` (links derive from `Brevo:AppBaseUrl`, falling back to the configured LoginUrl's origin); **§9.9 recorded decision:** invitations/cancellations/results are ESSENTIAL and always send — only the reminder email honours the new opt-out (in-app notices always land), enforced at enqueue, with a Profile toggle over `GET/PUT /api/me/email-preferences`. **Verification:** `dotnet test FcDraft.sln` → 297 (201 unit, 56 hermetic integration, 40 PostgreSQL) — new proofs include the §17.8 done-when (a completed draft's results are **byte-identical after importing and activating a different dataset version**), notifications surviving an API "restart" (fresh host, same database) with persisted read stamps, scoped mark-read (404-not-403), the opt-out suppressing only the reminder email, and a simulated Brevo outage never failing the cancelling mutation; `npm run test:run` → 62 (room, sheets, shortlist, rail, results, notification centre); both production builds green. The next session is **PR-21 — Admin communications, draft operations, and audit views**.

**PR-21 completed (16 July 2026):** Completed MVP administration and recovery — admin communications, real draft operations, and the immutable audit views (§8.2, §9.8's admin half, §9.10, §17.8). **Announcements:** compose → preview → explicit confirmation over `POST /api/admin/announcements/preview` and `POST /api/admin/announcements`; the preview resolves the audience (all ACTIVE players, or the active participants of one draft) and reports sender plus the §9.9 opt-out split; the confirmed send re-resolves the audience and returns **409 when the count moved since the preview** — the server-side proof of §9.8's confirmation step. One transaction commits the append-only `announcements` campaign record (subject/body/audience definition/counts/requester/time), every in-app notification (PR-20 pipeline — opted-out players still get the notice; the announcement is exactly the OPTIONAL email class the PR-20 `optional_email_opt_out` flag exists for, enforced at enqueue), the AnnouncementSent audit record, and the outbox emails — through the EXISTING durable outbox as a new string-stored `EmailKind.Announcement`, each row stamped with the campaign id and **throttled** by staggering `next_attempt_at` in batches of 20 per 15-second window (the worker's own cadence), so a bulk send drains steadily instead of bursting at Brevo (whose payload carries the campaign id as a tag). Delivery visibility: `GET /api/admin/announcements` lists campaigns with live pending/sent/failed tallies; the in-memory branch records inline outcomes in a new `InMemoryEmailOutbox` ledger so `GET /api/admin/email-outbox` and the tallies work on BOTH storage branches, and a simulated Brevo outage never fails a confirmed send. Forward-safe migration `AddAnnouncementsAndCampaignDelivery`: the `announcements` table, nullable `email_outbox.campaign_id` (+index), and `payload` widened 2048→4096. **Draft operations:** `/admin/drafts` grew from the read-only list into real operations — an inspect dialog (state, version, participants, full append-only event history) plus pause/resume/cancel built on the EXISTING PR-16 commands with `ActorIsAdmin` (no new command surface); pause/cancel capture a required reason, every action is version-checked (a stale 409 resyncs the snapshot), and recovery stays compensating — proven by a PostgreSQL test that re-reads the pre-existing event rows **byte-for-byte identical** after an admin pause/resume round-trip, with the new events appended gap-free. **Audit views:** `GET /api/admin/audit/draft-events` (filters: draft, type, actor, date; actor names resolved) and `GET /api/admin/audit/security-events` (action, user, email, date) over the two append-only trails, admin-only; §9.10's unrecorded admin actions now land in the trail attributed to the ACTING admin (id + email + IP, target in the detail): user create/update/invite/activate/deactivate, dataset import/activation, template create/activation, five-star curation, and every bulk announcement — new string-stored `SecurityAuditAction` members, no migration needed. No mutation verb exists on any audit surface (proven: PUT/DELETE/POST never reach a handler). **Navigation/UX:** new Communications and Audit Log admin modules behind `RequireAdmin`; per §12.4 the deferred pieces render disabled with “Coming soon” — the §8.2 Overview dashboard as an inert nav item and Brevo-TEMPLATED announcements as a disabled control (plain announcements ship). Fixed a latent `useFocusTrap` bug the reason-capture forms surfaced: the trap re-initialized on every render and stole focus mid-typing in ANY dialog form; it now initializes once per mount. **Verification:** `dotnet test FcDraft.sln` → 322 (213 unit, 67 hermetic integration, 42 PostgreSQL) — new proofs cover the §17.8 done-when: throttled windows measured on the outbox rows (20 + N, 15 s apart; one worker pass delivers only the due batch), the confirmed-count 409, the opt-out suppressing only the email, every admin action attributable (actor + reason in both trails), audit immutability (fresh-scope byte-identical baseline; no update/delete route), and campaign records + tallies surviving an API restart; `npm run test:run` → 78 and `npm run test:e2e` → 6; both production builds green. The next session is **PR-22 — PWA lifecycle, accessibility, performance, and observability**.

**PR-22 completed (16 July 2026):** Release hardening delivered — PWA lifecycle, device ergonomics, observability, and measured performance/accessibility (§17.9, §12.1, §12.2, §14). The §18 stale-shell risk is closed by a **client/API version handshake**: a compiled-in contract on both sides (drift fails CI), `X-DraftRoom-Contract` on every `/api` response, anonymous `GET /api/meta/version` (`{contract, revision}`), and a refresh prompt driven by both the `virtual:pwa-register` `onNeedRefresh` flow (hourly + foreground update checks) and any observed contract mismatch. Authenticated API data is never cached: `Cache-Control: no-store` on all `/api` responses, workbox `navigateFallbackDenylist` for `/api|/hubs|/health|/swagger`, empty runtime caching — proven by integration tests and an e2e parse of the generated `dist/sw.js`. Offline is an explicit state (live-region banner on every journey; mutations rejected at the axios seam before anything is sent; the pick sheet disables its confirm with an explanation; reads pass). Install guidance ships in-product after the user has value (hub card + profile panel; captured `beforeinstallprompt`; iOS Share → Add to Home Screen steps). Ergonomics: safe-area insets on every exposed edge, `--keyboard-inset` via `visualViewport` keeps the draft action bar above the iOS keyboard, and landscape short-viewport rules keep the clock visible. Observability: per-request correlation ids flowing request → MediatR logging behavior → response header (and into ProblemDetails), JSON console logs with scopes in Production, vendor-neutral `System.Diagnostics.Metrics` instruments, and an `IErrorReporter` seam (default logs+counts; no vendor lock); `/health` reports contract/revision plus a `self` check on both storage branches. Measured §14 (recorded in `fc-draft-web/docs/PR22_EVIDENCE.md` with the manual review matrix and real-app screenshots): Slow-4G shell load **1.36 s** (budget 3 s), authenticated reads p95 **≤2.6 ms** locally, pick propagation p95 **0.2 ms** after accept; entry bundle cut to 96 KB gzip by route-splitting admin/results/explorer. Accessibility: axe automated checks (Playwright both themes + component scans) fixed a prohibited brand `aria-label` and 3.6:1 magenta eyebrows (new AA `--color-secondary-text` ramp); `banner-dismiss` gained a 44 px hit area. Corrections: PR-21's frontend count was 76, not 78. Verified: 336 backend / 103 vitest / 14 e2e, builds green. The next session is **PR-23**.

**PR-23 completed (16 July 2026):** End-to-end MVP verification and the private-beta release package (§17.9, §16). **Multi-client full-stack E2E:** a second Playwright harness (`fc-draft-web/playwright.fullstack.config.ts`, `npm run test:e2e:full`) boots the REAL stack — the API via `dotnet run` in environment **Testing** (in-memory branch, seeded accounts, Brevo deliberately unconfigured; never Development) behind the production `vite preview` build through the PR-22 proxy seam (now parameterized by `DRAFT_API_ORIGIN`; dedicated ports 5089/4174) — and drives complete **1v1 (two clients) and 2v2 (four clients) drafts through the real UI**: lobby → invite/join → (2v2 seeds + Seed1/Seed2 pairing) → ready → host-only start → spinner → club/protect round → the full 30-pick snake position draft → results, asserting cross-client live propagation at every stage and that non-hosts never see host controls. **Concurrency/race + resilience, end-to-end through real clients** (`e2e-full/resilience.spec.ts`): simultaneous 2v2 teammate confirmations of different players → exactly one accepted pick, the loser shown the §6.5 explanation and auto-resynced; a client dropped offline mid-draft reconnects (§7.4) to the authoritative snapshot showing the four picks it missed with zero duplicates; and a Brevo-outage drill — a mid-draft cancellation commits, propagates live, lands in-app notices, and the admin outbox view reports the FAILED sends. CI gains an `e2e-full` job (with .NET) and a `container` job building the production Dockerfile from a clean checkout on every run; the 14 client-only checks are untouched. **Acceptance evidence:** [`fc-draft-web/docs/PR23_EVIDENCE.md`](fc-draft-web/docs/PR23_EVIDENCE.md) links EVERY §16 criterion (all 13) to its automated tests and/or recorded manual evidence — nothing unlinked; the operator-run physical-device sessions have a prepared kit ([`PR23_DEVICE_SESSIONS.md`](fc-draft-web/docs/PR23_DEVICE_SESSIONS.md): session matrix, 13-step checklist, capture template) and `scripts/seed-demo-lobby.mjs` seeds a ready lobby. **Seed/demo data:** `Database:SeedDemoAccounts` (default false, BOTH storage branches) seeds three deterministic demo players so a 2v2 lobby has its 4+ activated accounts without live email. **§15 analytics:** a new vendor-neutral `IProductAnalytics` seam (mirroring PR-22's `IOperationalMetrics`; same `FcDraft.DraftRoom` meter, no vendor lock) instruments invite-to-activation, lobby-to-start, completion outcomes, time-to-first-pick, pick/turn durations with the auto-pick (expiry) tag, admin-vs-host interventions, hub joins vs reconnects (new `RejoinDraft` hub method used by the client's reconnect handler), and email delivery outcomes on both branches — with a unit-proven privacy whitelist: only format/outcome/action tags, never ids, emails, passwords, tokens, or content (PWA-install rate and email click-through recorded as deliberately deferred — not server-measurable). **Release collateral:** the §12.3 **retention/deletion policy is now DEFINED** ([`RETENTION_POLICY.md`](RETENTION_POLICY.md) — governing requirement updated per §17.10.4) with an erasure-as-pseudonymization procedure; [`RUNBOOK.md`](RUNBOOK.md) documents the REAL operations path (Cloud Run us-east4 + Neon + WIF auto-deploy, rollback, backup/PITR recovery, single-instance caveats, purge/erasure SQL); [`DEPLOYMENT.md`](DEPLOYMENT.md) was restructured so Cloud Run + Neon is the primary documented path and the Render blueprint is an explicitly demoted legacy appendix (`render.yaml` header marked accordingly); and [`PRIVATE_BETA_CHECKLIST.md`](PRIVATE_BETA_CHECKLIST.md) gates the launch. **Reproducible build proven:** fresh `git clone` → `dotnet test` 341/341 (incl. all 42 Testcontainers PostgreSQL tests) → `npm ci`/103 vitest/production build → `docker build` → the image serves `/health` 200 and the shell with only `PORT` injected — plus the same container build running in CI continuously. **Verification:** `dotnet test FcDraft.sln` → **341** (222 unit, 77 hermetic, 42 PostgreSQL), `npm run test:run` → **103**, `npm run test:e2e` → **14**, `npm run test:e2e:full` → **5**, both production builds green. The remaining §17.9 done-when item — repeated real-iPhone/desktop sessions — is operator-run with the prepared kit and recorded in the evidence documents; the private beta then proceeds via the checklist.

**PR-24 completed (17 July 2026) — post-MVP:** Delivered the §8.2 admin **Overview dashboard**, the first of the two intentionally-deferred `Coming soon` controls to ship. A new admin-only `GET /api/admin/overview` (thin controller → `GetAdminOverviewQuery` MediatR handler) composes a read-only user/draft/engagement summary plus attention alerts from the EXISTING stores, so it reads identically on both storage branches. Because the PR-23 §15 analytics meter is **write-only** (fire-and-forget instruments with no query surface), the engagement figures are re-derived from the append-only draft-event trail instead: two small cross-branch reader additions — `IDraftEventReader.CountByTypeAsync` (event-type histogram; SQL `GROUP BY` on the EF branch, LINQ over the in-memory aggregates) and `IEmailOutboxReader.GetStatusTalliesAsync` (whole-outbox pending/sent/failed) — feed lobby→start and completion conversion, pick volume, the timer auto-pick rate, and email-delivery health; user tallies ride the existing directory search. The dashboard surfaces alerts (failed emails, paused drafts, accounts awaiting activation) and a live drafts-by-status breakdown, built entirely from the existing `stat-grid`/`panel` design-system primitives. The §8.2 Overview nav item flips from a disabled `Coming soon` span to an active link (§12.4 now has one fewer deferred control — Brevo-templated announcements remain). No migration (read-only over existing tables). **Verification:** `dotnet test FcDraft.sln` → **344** (222 unit, 79 hermetic integration, 43 PostgreSQL — new integration tests prove admin-only access + the derived summary/alerts, and a new PostgreSQL test proves the EF `GROUP BY` aggregations translate to SQL); `npm run test:run` → **107** (new `AdminOverviewPage` render/alerts/refresh tests); both production builds green; the live page verified end-to-end against a Testing-environment API driving a completed 1v1 draft (100% conversion, 30 picks, the email-failed alert rendered). Data-source research for the two remaining requests (player Roles/Role++ and 4.5★ Kick Off clubs) is complete and awaiting a source decision before any import; nothing was imported or fabricated.

**PR-25 completed (17 July 2026) — post-MVP:** Broadened the pre-draft club round from **five-star only** to **five-star or 4.5-star** EA FC 26 men's Kick Off clubs (a §5/§9.5/§16.6 + DRAFT_RULES decision 3 scope change, governing requirements updated first per §17.10.4). EA's feed omits club star ratings, so the default eligible set is transcribed from EA's official men's club-rating reveal — **7 five-star + 9 four-and-a-half-star = 16 clubs** — in the bundled dataset's exact spellings (guarded by a unit test). This also **fixes a latent bug**: the previous seed listed `Bayer 04 Leverkusen`, but the dataset club is `Leverkusen`, so the 4.5★ club was silently never offered; three other seeded names (`OM`/`Inter`/`AC Milan`) likewise didn't match the dataset and, being sub-4.5★ anyway, are dropped from the default (an admin can still curate any club eligible via the existing Templates UI). The `IsFiveStarEligible` flag, the `ListFiveStarClubsAsync` seam, and the `/admin/clubs/{id}/five-star` route keep their names for API stability but now mean "eligible 5★/4.5★"; user-facing copy changed ("Five-star club" → "Elite club (5★ or 4.5★)"). 16 eligible clubs still comfortably exceed the max team count (1v1 ≤ 10, 2v2 ≤ 8), so per-lobby club uniqueness holds. No migration (data/wording only). **Verification:** `dotnet test FcDraft.sln` → 348 (new `EligibleClubsTests` proves the 16-club set + the Leverkusen fix + sub-tier exclusion); `npm run test:run` → 107 (club-selection label updated); both production builds green. **Roles/Role++** for all players remains the open item — the source decision (WeFUT low-volume crawl) is made; the importer is the next slice.

**PR-26 completed (17 July 2026) — post-MVP:** Populated player **Roles / Role+ / Role++** for the FC 26 dataset — the last deferred §18 data slice (the club-star item shipped in PR-25). EA's public feed omits per-position role familiarity, so it is backfilled from the approved secondary source **WeFUT** (`wefut.com/roles`), realizing DRAFT_RULES decision 9 / §5 rule 9 ("roles supplemented from secondary sources") with no governing-requirement change needed. Two committed Node scripts mirror `import-fc26-players.mjs` conventions: `crawl-wefut-roles.mjs` enumerates all **49 roles × 2 tiers** (index-driven catalog; paginated; **on-disk cache, resumable, ~1.2 s throttle** — a one-time ~870-page cached crawl, never continuous, source credited per §18), and `apply-wefut-roles.mjs` matches onto the dataset and emits a match-quality report ([`WEFUT_ROLES_IMPORT.md`](WEFUT_ROLES_IMPORT.md)). **The join is exact, not fuzzy:** every WeFUT card exposes `data-base-id`, which *is* the EA player id our dataset keys on — so no name/club/OVR heuristic was needed (0 name-cross-check mismatches across 1716 matches). **Role familiarity is card-level and promo cards inflate it**, so only the **base card** is trusted — a standard-rarity card (Gold / Gold Rare) at the player's base OVR; those rarities appear exclusively at base OVR in the crawl, cleanly excluding special cards that merely share it (a crawler dedup fix keys on card class so a same-OVR promo can't clobber a base row — e.g. Van Dijk keeps both Defender+ *and* Ball-Playing Defender++). **Result:** 1716 of 1748 players carry roles (3907 entries — 3756 Role+, 151 Role++); the 32 unmatched (12 promo-only, 20 with no WeFUT presence) are **left empty, never guessed**. Roles land in the display shape the UI already renders — `{ position, name, familiarity: 0|1|2 }` — written to **both** dataset copies (frontend `public/data` + backend embedded resource), byte-identical; `import-fc26-players.mjs` now carries the roles overlay forward so re-importing EA base data no longer wipes it. The Player Explorer empty-state copy was corrected (it no longer claims roles are universally absent). No migration (the `RolesJson` jsonb column already existed). **Verification:** `dotnet test FcDraft.sln` → **352** (230 unit incl. new `DatasetRolesTests` loading the real bundled dataset through the real parser — asserts role shape, Haaland's Advanced Forward++, and the Van Dijk dedup regression guard; 79 hermetic integration, 43 PostgreSQL); `npm run test:run` → **107**; `npm run test:e2e` → **14**; `npm run test:e2e:full` → **5**; both production builds green; verified end-to-end against a **Testing**-environment API (`GET /api/players/239085` and `/203376` return the roles in the rendered shape); base-card roles were confirmed against the live WeFUT player pages (Haaland, Van Dijk, De Bruyne — exact, both tiers).

**Deployment note (16 July 2026):** The live deployment runs on **Google Cloud Run** (service `the-draft-room`, region `us-east4`, project `909367690008`) backed by **Neon** PostgreSQL — **not** Render, despite the committed `render.yaml`/`DEPLOYMENT.md` (a legacy/alternative path, not the live target). Manual `gcloud run deploy --source .` is being replaced by **continuous deployment**: a GitHub Actions workflow (`.github/workflows/deploy-cloud-run.yml`) builds the repo `Dockerfile` and deploys to Cloud Run on every push to `main`, authenticated with keyless **Workload Identity Federation** (no long-lived keys) and gated on the CI workflow passing. The service stays single-instance (`--max-instances 1`) because live draft state is in-memory. §12.1 Hosting updated. No numbered roadmap PR is affected; the next session remains **PR-14**.

**v0.15 update (15 July 2026):** Fixed the administrator identity. **`mdevansh@gmail.com` is now the single designated administrator account** — it replaces the placeholder `admin@draftroom.dev` across the seeded in-memory identity store and the EF Core `DatabaseInitializer` bootstrap seed, the login-screen prefill/development-access note, the backend integration/database test constants and the unit-test assertion, and the README/DEPLOYMENT credentials. The seeded password (`DraftAdmin@2026`) is unchanged and remains public in this repo, so it must be changed on first production login (see [DEPLOYMENT.md](DEPLOYMENT.md) Step 5). Additionally, **Name and email are now the two mandatory fields when adding a user** (§7.1, §9.2): the Admin → Users create form requires a Name input alongside the email, and `POST /api/users` now rejects a blank display name with a `400` instead of deriving one from the email local-part. Verified with both production builds green, `dotnet test FcDraft.sln` and `npm run test:run` passing, and a scripted API drive of seeded-admin login `200` plus create-user validation (name + email required). No numbered roadmap PR is complete; the next session remains **PR-04**.

---

## 1. Product summary

The Draft Room is a private, live multiplayer drafting app for groups that want to build football squads using the FC 26 men's player set.

An administrator creates and manages users. Players can create or join a multi-team tournament lobby, form draft teams, and take turns selecting footballers. A draft can be played as:

- **1v1:** up to 10 people join a lobby; each person represents one solo draft team. A later in-game tournament match has two people playing at a time.
- **2v2:** up to 16 people join a lobby and form up to eight two-person draft teams. Each team contains one **Seed 1** player and one **Seed 2** player. A later in-game tournament match has four people playing at a time.

Drafting happens one position at a time. All participants see picks, availability, turn state, timers, and team progress update live. The product should feel like a premium football broadcast: energetic, competitive, animated, and easy to operate with one hand on an iPhone.

## 2. Product vision

Make small-group football drafts feel like a live sporting event rather than a shared spreadsheet or group chat.

The product wins when a tournament group can open an invite, form fair teams, complete a draft without arguing about attendance, order, whose turn it is, or whether a player is available, and immediately see every completed squad.

## 3. Goals and non-goals

### 3.1 MVP goals

1. Let an admin securely create, activate, and deactivate users.
2. Support complete 1v1 and 2v2 draft sessions.
3. Let the lobby host seed participants and create balanced 2v2 teams using Seed 1 + Seed 2 pairing.
4. Enforce turn order, position eligibility, unique picks, and roster rules server-side.
5. Synchronize a live draft across iPhone and desktop clients.
6. Send transactional and group emails through Brevo.
7. Deliver an installable, responsive PWA with a premium football identity.
8. Preserve a permanent, auditable history of every draft action.

### 3.2 Non-goals for MVP

- Public registration or anonymous play.
- Women's footballers, Icons, Heroes, Ultimate Team special cards, custom cards, or historical player datasets.
- Building or running the in-game tournament bracket; the selected squads will be used manually in FC 26 Kick Off mode.
- Gameplay, match simulation, or integration with an FC console account.
- Public matchmaking, global leaderboards, chat, trading, auctions, or payments.
- Native iOS or Android applications.
- Automated skill measurement from gameplay data.
- Multi-competition tournament brackets.

## 4. Users and roles

| Role | Description | Primary permissions |
|---|---|---|
| Admin | Operates the private community | Manage accounts, import/manage player data, view drafts, configure defaults, send email announcements, view audit records |
| Player | Participates in drafts | Manage own profile/password, create or join permitted lobbies, ready up, make picks for their team, view squads and history |
| Lobby host | A contextual responsibility held by the player who creates a lobby | Invite participants, assign Seed 1/Seed 2 in 2v2, form teams, run the order spinner, start/pause/resume/cancel the draft |

An admin may also participate as a player, but the app must make it explicit whether they are acting as **Admin** or **Player** in a session.

## 5. Confirmed product rules and remaining assumptions

The following rules are now confirmed:

1. **Private access:** there is no self-registration. A single designated administrator account (**`mdevansh@gmail.com`**) is the only admin; every other account is a player it creates. An admin creates a user and the server issues a **unique one-time temporary password per invite** (resolved in PR-05; supersedes the earlier fixed `Draft@1234`), which Brevo emails. The user must change it after first sign-in, and a token authenticated with the temporary password may reach only the forced password-change endpoint.
2. **Lobby capacity:** 1v1 supports 2–10 people, with one solo draft team per person. 2v2 supports 4–16 people in even-numbered increments, with two people per draft team.
3. **Host ownership:** the lobby creator is the host. The host verifies attendance, controls Seed 1/Seed 2 assignment for 2v2, forms teams, and is the only participant who can start the draft.
4. **2v2 formation:** each draft team must have exactly one host-designated Seed 1 player and one host-designated Seed 2 player.
5. **Draft order:** after teams are formed, a server-authoritative random spinner ranks every draft team. The revealed order is saved and used for the club-selection round and player-pick rounds.
6. **Club-selection round:** in spinner order, each draft team chooses an eligible real-world FC 26 Kick Off club rated **five stars or 4.5 stars** and protects one eligible footballer from that club.
7. **Position rounds:** selection starts at `ST`, followed by `LW`, then `RW`, and continues through the approved formation sequence. Every draft team receives one 120-second turn for the active position.
8. **Eligibility:** the active-position pool shows only men's base/Kick Off footballers rated 75 or higher whose primary or alternate position matches the slot.
9. **Player detail:** every visible card exposes overall rating, card stats, primary and alternate positions, role familiarity including `+`/`++`, PlayStyles, club, league, and nation.
10. **Excluded content:** women, Icons, Heroes, Ultimate Team special cards, and custom/historical cards are excluded.
11. **Real-time requirement:** lobby presence, seeds, teams, spinner result, club choices, protected players, timer, picks, and squad state update live for every connected client.

The following draft rules were open in earlier drafts and are now **locked in PR-01**. The authoritative matrix, roster template, acceptance examples, and derived database constraints live in [`DRAFT_RULES.md`](DRAFT_RULES.md); the summary is:

1. **2v2 pick authority:** either teammate may confirm the team's pick during its turn; the first valid server-accepted submission wins.
2. **Held player:** each team drafts one protected footballer from its chosen elite club into a **separate, dedicated squad slot** (a 12th member outside the starting XI). The held footballer is removed from every team's pool but does not fill or skip a formation position.
3. **Club eligibility & uniqueness:** eligible clubs are EA FC 26 men's **five-star or 4.5-star** Kick Off clubs; each eligible club may be chosen by only one draft team in a lobby.
4. **Round order:** position and bench rounds use **snake** order (reversing each round); the pre-draft club/held round uses straight spinner order.
5. **Squad shape:** 16 footballers per team — 1 held player, an 11-player 4-3-3 starting XI drafted `ST → LW → RW → CM → CM → CM → LB → CB → CB → RB → GK`, then 4 flexible (any-position) substitutes.
6. **Footballer uniqueness:** a footballer, once held or drafted, is globally unavailable to every team in the lobby.
7. **Timer expiry:** on a 120-second expiry the server auto-picks the highest-rated available eligible footballer for the active slot.
8. **Host permission:** any active (non-deactivated) user may create and host a lobby.
9. **Data source:** the EA public FC 26 ratings feed is authoritative; Role/Role++ and PlayStyles are supplemented from secondary sources; licensed media is deferred until rights are confirmed.

Remaining operating assumption: all active participants are expected to have a network connection. Offline mode supports shell loading and safe cached views, not offline picking.

## 6. Core product rules

### 6.1 Draft lifecycle

`Draft → Lobby → Seeding/team formation → Ready check → Spinner ranking → Club selection/protected player → Position draft → Completed`

Exceptional states are `Paused`, `Cancelled`, and `Abandoned`.

- Rules and roster templates may be edited only before the ready check completes.
- A draft starts only when the host confirms that all expected participants are present, all participants are assigned, and every team is ready.
- Only the lobby host may start the spinner and advance the lobby into the live draft.
- Draft picks are valid only while the session is `PositionDraft` and it is the acting team's turn.
- A completed draft is immutable. Admin corrections create explicit audit events; they do not rewrite history silently.

### 6.2 Format rules

| Rule | 1v1 | 2v2 |
|---|---|---|
| Human participants | 2–10 | 4–16, even numbers only |
| Draft teams | 2–10 solo teams | 2–8 paired teams |
| Humans per draft team | 1 | 2 |
| Later Kick Off match size | 2 people | 4 people |
| Seed constraint | None | Exactly one host-assigned Seed 1 + one host-assigned Seed 2 per team |
| Pick ownership | Individual | Shared by teammates |
| Squad per draft team | One | One shared squad |

### 6.3 Position eligibility

- The default MVP template is 4-3-3: `ST → LW → RW → CM → CM → CM → LB → CB → CB → RB → GK`, followed by 4 flexible (any-position) substitute slots, plus one held-player slot filled in the pre-draft round (see [`DRAFT_RULES.md`](DRAFT_RULES.md)).
- Position and bench rounds run in **snake** order; the pre-draft club/held round runs in straight spinner order.
- Each concrete roster slot has one required position. A footballer is eligible when their primary or an alternate FC 26 position matches the active slot; a flexible substitute slot accepts any position.
- Only eligible men's base/Kick Off footballers with an overall rating of **75 or higher** appear in the position pool.
- The UI excludes or clearly disables already-protected and already-drafted footballers.
- The API remains authoritative and rejects stale, duplicate, out-of-turn, or ineligible picks.

### 6.4 Timer and missed turns

- Pick timer: **120 seconds per draft team for each position**.
- Warning state begins at 15 seconds.
- On expiry the server **auto-picks** the highest-rated available eligible footballer for the active slot (tie-break: overall rating, then name, then stable id), records an auto-pick event, and advances the turn.
- Auto-pick is a deterministic pure function of the available pool and slot eligibility, so it is reproducible in tests and after reconnection.

### 6.5 Concurrency

- The server accepts only one successful pick for a turn/version.
- Every command includes the draft version last seen by the client.
- A version mismatch returns a conflict; the client refreshes state and explains that another action won the race.
- Reconnection restores the authoritative session snapshot and subsequent live events.

## 7. End-to-end user journeys

### 7.1 Admin creates a player

1. Admin opens **Users** and selects **Add player**.
2. Admin enters the player's name (display name) and email — **both are mandatory**; the form and `POST /api/users` reject a submission missing either. The system assigns the initial temporary password `Draft@1234`.
3. The system validates email uniqueness and creates the account.
4. Brevo sends a branded welcome/invite email containing the portal link, temporary password, and mandatory password-change instructions.
5. The player signs in and must set a private permanent password before accessing the app.

### 7.2 Create and launch a 1v1 draft

1. A permitted player selects **New lobby**, chooses 1v1, and becomes host.
2. The host invites between 2 and 10 active users and selects a roster template.
3. Expected players join, confirm presence, and ready up as solo draft teams.
4. The host starts the server-authoritative spinner, which produces and reveals the team order.
5. In spinner order, each team selects an available eligible club (five-star or 4.5-star) and protects one eligible footballer from it.
6. The app opens the `ST` round. Each team receives 120 seconds to make an eligible pick, then the draft advances through `LW`, `RW`, and the remaining configured positions.
7. The result page shows every squad, pick timeline, and summary statistics for the later Kick Off tournament.

### 7.3 Create and launch a 2v2 draft

1. A player creates a 2v2 lobby, becomes host, and invites 4–16 users in even-numbered increments.
2. The host assigns every present participant to Seed 1 or Seed 2; all clients see changes live.
3. The host forms up to eight teams. The system blocks any team that does not contain exactly one Seed 1 and one Seed 2 participant.
4. Participants confirm teams and ready up. Only the host can start.
5. The host starts the spinner; the server randomizes and permanently records the order of all formed teams.
6. In spinner order, each team chooses an available eligible club (five-star or 4.5-star) and protects one eligible footballer from it.
7. The position draft begins at `ST`, then `LW`, then `RW`. Both teammates see the same eligible pool, player details, shortlist, timer, and shared squad.
8. The first valid team pick accepted by the server updates every participant's board in real time.

### 7.4 Reconnect to a live draft

1. The app detects a lost connection and shows a non-blocking reconnect state.
2. On reconnection, it fetches the latest server snapshot.
3. It reconciles picks, active position, timer, and current team.
4. The user returns to the live room without duplicating an action.

## 8. Information architecture and navigation

### 8.1 Player navigation

| Navigation item | Module | Purpose |
|---|---|---|
| Home | Dashboard | Next action, active draft, pending invites, recent results |
| Drafts | Draft Hub | Active, upcoming, and completed drafts; create draft |
| Teams | Squad Archive | Completed squads and side-by-side comparisons |
| Players | Player Explorer | Search and filter the FC 26 men's dataset |
| Profile | Account | Personal details, password, PWA and email preferences |

### 8.2 Admin navigation

| Navigation item | Module | Purpose |
|---|---|---|
| Overview | Admin Dashboard | User, draft, and engagement summary; alerts |
| Users | User Management | Add/edit/deactivate users, assign seeds, reset credentials |
| Drafts | Draft Operations | Create, inspect, pause, resume, cancel, and audit drafts |
| Player Data | Dataset Management | Import/version the dataset, validate fields and images |
| Templates | Draft Configuration | Roster templates, position sequence, timers, turn rules |
| Communications | Brevo Email Centre | Send announcements and inspect transactional delivery status |
| Audit Log | Audit & Security | Account, configuration, and draft events |
| Settings | Platform Settings | Branding, email sender, PWA metadata, security defaults |

### 8.3 Responsive navigation behavior

- **Desktop/tablet landscape:** persistent collapsible left sidebar, top status bar, main content canvas.
- **iPhone:** bottom navigation for the four highest-frequency player destinations; profile and secondary items live in a “More” sheet.
- **Live draft on iPhone:** the draft room replaces global bottom navigation with a dedicated sticky action area to avoid accidental exits.
- **Admin mobile:** compact drawer navigation. Dense import and audit tasks remain responsive but are optimized for desktop.

## 9. Functional modules and requirements

### 9.1 Authentication and account security

**MVP requirements**

- Email and password sign-in.
- Role-gated routes and API endpoints.
- Admin-created accounts only.
- Temporary-password or secure invite activation flow.
- New accounts use `Draft@1234` as the initial temporary password and must change it at first sign-in.
- Password, temporary-password, new-password, and confirmation fields include accessible show/hide eye controls.
- Users can change their password from **Profile → Security** after activation.
- Forgot/reset password email through Brevo.
- Logout and logout-all-sessions.
- Token revocation after password reset, password change, account deactivation, or admin security action.
- Rate limiting and temporary lockout for repeated failed sign-ins.
- Audit events for sign-in, failed sign-in, credential reset, activation, and deactivation.

### 9.2 User and seed management

- A single designated administrator account (**`mdevansh@gmail.com`**) is the sole admin; it is the account seeded on bootstrap, and all other accounts are players.
- Create, view, edit, activate, and deactivate users.
- Creating a user requires **name (display name) and email as mandatory fields**; both are validated on the client and by `POST /api/users`, which no longer derives a name from the email local-part.
- Required user fields: display name, normalized unique email, role, status, and must-change-password state.
- Optional avatar and preferred team name.
- Bulk CSV import is post-MVP; MVP supports one-at-a-time creation.
- Seed 1/Seed 2 is a lobby-scoped assignment made by the host for each 2v2 event, not a permanent admin-owned user attribute.
- A deactivated user cannot sign in or join a new draft; historical attribution remains intact.

### 9.3 Player dataset

- Import a versioned snapshot of FC 26 men's footballers.
- Minimum fields: external ID, common name, full name, overall rating, individual card stats, primary/alternate positions, positional roles and `+`/`++` familiarity, PlayStyles, club, club star rating, league, nation, image reference, base/Kick Off eligibility, active flag, and dataset version.
- Search by player name with typo-tolerant or prefix matching.
- Filter by position, overall rating range, club, league, and nation.
- Sort by overall rating and name.
- Default draft pools enforce overall rating `>= 75`.
- Clearly mark protected, drafted, and otherwise unavailable footballers during a draft.
- Exclude women, Icons, Heroes, Ultimate Team special cards, and other non-Kick Off content at import/activation and query boundaries.
- An in-progress draft remains pinned to its starting dataset version.
- Import validation reports duplicates, missing IDs, invalid positions, and malformed rows before activation.

### 9.4 Draft creation and lobby

- Create a named 1v1 or 2v2 tournament lobby; its creator becomes host.
- Select up to 10 participants for 1v1 or up to 16 participants for 2v2.
- Enforce a minimum of 2 participants for 1v1 and 4 participants for 2v2; 2v2 participant counts must be even.
- Configure the roster template; the MVP pick timer is fixed at 120 seconds per team.
- Show invitation/join state for every slot.
- Let the host assign Seed 1/Seed 2 and form teams in 2v2.
- Enforce exactly one Seed 1 and one Seed 2 participant per formed 2v2 team.
- Ready/unready control for each participant.
- Host may remove/replace a participant before start.
- Only the host sees an enabled **Start draft** control, and it remains disabled until attendance, team, and readiness rules pass.
- Freeze configuration when the draft begins.

### 9.5 Spinner and club-selection round

- The host starts the order spinner only after all teams are valid and ready.
- Randomization occurs on the server using an unbiased shuffle; the wheel is an animated reveal of the committed result, not the source of truth.
- Reveal and store the complete order for all 2–10 solo teams or 2–8 paired teams.
- After ranking, teams select in order from FC 26 men's Kick Off clubs rated five stars or 4.5 stars.
- Show club name, crest only when licensed, league, representative formation, and eligible squad members.
- Each team protects one eligible player rated 75+ from its chosen club.
- Club choice, protected player, remaining options, and active team update in real time.
- The protected footballer is locked before the first `ST` round begins.

### 9.6 Live draft room

- Show active position, current team, timer, round/slot progress, and connection status.
- Search/filter eligible footballers without leaving the room.
- Player cards show name, image, overall rating, card stats, primary/alternate positions, roles with `+`/`++`, PlayStyles, club, league, and nation.
- The active pool contains only undrafted/unprotected eligible players rated 75+ for the current position.
- A selection requires a confirmation step that names the draft team and roster slot.
- Update picks for all connected clients in real time.
- Show every team through a compact team rail and provide focused squad, filled/empty slots, recent picks, and full pick history views without attempting to render all 10 full squads at once on iPhone.
- Provide a personal (per-user) shortlist/favourites as a planning aid; teammate voting and chat are excluded from MVP (2v2 uses shared pick control per [`DRAFT_RULES.md`](DRAFT_RULES.md) decision 11).
- Host controls: start, pause, resume, and cancel with reason. Admin recovery remains separately permissioned and audited.
- Keyboard support on desktop and touch targets of at least 44×44 CSS pixels on mobile.

### 9.7 Squad results and archive

- Present completed squads in formation and list views.
- Show average rating, rating by line, clubs/leagues/nations represented, and pick sequence.
- Store immutable result snapshots so later dataset updates do not change historical squads.
- Allow participants and admins to reopen a completed draft in read-only mode.
- Shareable images, public links, and exports are post-MVP.

### 9.8 Email communications through Brevo

**Transactional templates**

- Account invitation/welcome.
- Password reset.
- Draft invitation.
- Draft reminder.
- Draft cancelled.
- Draft completed with result link.

**Admin communications**

- Send a plain or templated announcement to all active registered players.
- Preview subject, sender, audience count, and body before sending.
- Require a confirmation step for bulk send.
- Record campaign/message ID, sender, audience definition, requested time, and delivery status where available.
- Use a background job/outbox so a Brevo outage never rolls back a user or draft transaction.
- Store no Brevo API secret in frontend code or the database; load it from server environment configuration.

### 9.9 Notifications

- In-app notification centre for invitations, reminders, draft changes, and results.
- Unread badge and mark-read behavior.
- Email preferences for optional announcements; security and essential service messages remain mandatory where legally permitted.
- Web push notifications are post-MVP unless an iPhone PWA validation spike proves the experience reliable enough for launch.

### 9.10 Admin audit and recovery

- Append one immutable event for every draft transition and accepted pick.
- Record actor, action, timestamp, previous/new state, draft version, and reason where relevant.
- Record admin changes to users, templates, dataset activation, and bulk email requests; record host changes to lobby seeds, team membership, order, and draft controls.
- Admin correction uses a compensating action with a reason and linked event; it never deletes the original record.
- Admin may pause or cancel a stuck draft. Editing an accepted pick after play begins is excluded from normal MVP operation and reserved for an audited recovery flow.

## 10. Draft state and event model

### 10.1 Draft states

| State | Allowed next states |
|---|---|
| Draft | Lobby, Cancelled |
| Lobby | TeamFormation, Cancelled |
| TeamFormation | ReadyCheck, Lobby, Cancelled |
| ReadyCheck | SpinnerRanking, TeamFormation, Cancelled |
| SpinnerRanking | ClubSelection, Cancelled |
| ClubSelection | PositionDraft, Paused, Cancelled |
| PositionDraft | Paused, Completed, Cancelled, Abandoned |
| Paused | ClubSelection, PositionDraft, Cancelled, Abandoned |
| Completed | None |
| Cancelled | None |
| Abandoned | None |

### 10.2 Core events

- `DraftCreated`
- `ParticipantInvited`
- `ParticipantJoined`
- `ParticipantSeedAssigned`
- `TeamsFormed`
- `ParticipantReadied`
- `DraftStarted`
- `SpinnerOrderCommitted`
- `SpinnerOrderRevealed`
- `ClubSelected`
- `FootballerProtected`
- `PositionRoundStarted`
- `PickAccepted`
- `DraftPaused`
- `DraftResumed`
- `DraftCompleted`
- `DraftCancelled`
- `DraftAbandoned` (added in PR-10 to record the §10.1 `→ Abandoned` terminal transition)
- `AdminRecoveryApplied`

Events are append-only. Current draft state may be stored as a projection for fast reads, but history must never be reconstructed solely from the mutable current row. **Implemented in PR-10:** the `DraftEvent` entity persists this append-only history (one immutable, sequence-numbered event per accepted transition), and `DraftStateProjection.Replay` rebuilds/verifies the current status and version from it.

## 11. Conceptual data model

| Entity | Purpose |
|---|---|
| User | Identity, profile, role, account status, must-change-password state |
| LoginSecurity | Revocation, lockout, temporary credential, and password security state |
| Footballer | FC 26 men's player attributes, stats, positions, roles, and PlayStyles |
| PlayerDatasetVersion | Import identity, source metadata, activation state |
| Club | Kick Off club details, star rating, active dataset version |
| Draft | Format, status, host, rules, dataset version, current version |
| DraftParticipant | User membership, lobby seed assignment, ready/join state |
| DraftTeam | Solo/paired team identity, spinner order, selected club |
| DraftTeamMember | Participant-to-team assignment |
| ProtectedFootballer | Team's protected player and eventual roster-slot linkage |
| RosterTemplate | Ordered position slots and eligibility rules |
| DraftRosterSlot | Slot snapshot for a team in a specific draft |
| DraftPick | Accepted footballer, team, slot, pick number, actor, timestamp |
| DraftEvent | Append-only lifecycle and recovery trail |
| Notification | In-app message and read state |
| EmailOutbox | Reliable asynchronous Brevo work item |
| AuditEvent | Security and admin activity trail |

Important constraints include unique normalized email, maximum participants by format, exactly two members per 2v2 team, unique spinner rank per draft, one accepted pick per roster slot, one participant per draft membership, and optimistic concurrency on the draft version. Unique club and footballer constraints are applied once the remaining assumptions in §5 are confirmed.

## 12. Technical product requirements

The implementation will follow the supplied architecture and design documents.

### 12.1 Application architecture

- **Frontend:** React 18, TypeScript, Vite, React Router, Zustand, Tailwind CSS, accessible headless primitives.
- **Backend:** .NET 8, ASP.NET Core, Clean Architecture, CQRS with MediatR, FluentValidation.
- **Database:** PostgreSQL 14+ managed exclusively through EF Core migrations with explicit snake-case mappings.
- **Hosting:** one process, port, origin, and deployable bundle — ASP.NET serves the built React PWA and `/api` endpoints from a single container. The live deployment is **Google Cloud Run** (region `us-east4`, service `the-draft-room`) backed by a managed **Neon** PostgreSQL database. The service is pinned to a **single instance** (in-memory live-draft state and a shared SignalR/WebSocket process) and must not autoscale. A push to `main` **auto-deploys** via a GitHub Actions workflow that builds the repo `Dockerfile` and runs `gcloud run deploy`, authenticated with keyless **Workload Identity Federation** and gated on the CI suite passing. (The committed `render.yaml`/`DEPLOYMENT.md` describe an alternative Render path and are not the live target.)
- **API:** thin controllers dispatch commands/queries; business rules live in handlers/domain services.
- **Real time:** ASP.NET Core SignalR on the same origin, using authenticated draft groups and server-authored events.
- **Email:** a backend Brevo integration behind an application interface and durable outbox worker.
- **Observability:** structured logs, health endpoint, correlation IDs, error monitoring, and operational metrics.

### 12.2 Progressive Web App

- Valid web app manifest with name, short name, theme colors, icons, and standalone display mode.
- Service worker caches the versioned app shell and safe static assets.
- Do not cache authenticated API responses containing personal or live draft data unless explicitly designed for it.
- Show an offline/reconnecting state rather than allowing offline mutations.
- Provide an in-product install prompt only after the user has received value; include iOS-specific Add to Home Screen guidance.
- Handle safe-area insets, standalone display mode, orientation changes, and iPhone virtual keyboard behavior.
- A new deployment must not leave clients stuck on an incompatible cached shell; prompt for refresh when a new version is ready.

### 12.3 Security and privacy

- TLS in every deployed environment.
- BCrypt password hashing and secure reset tokens.
- Authorization enforced server-side for every command and query.
- Secrets stored only in environment/server configuration.
- Validate imports and encode user-authored content.
- Rate-limit authentication, invitations, bulk email, and high-frequency draft actions.
- Avoid exposing full participant email addresses in draft rooms.
- Hash the temporary password exactly like any other password; never persist or log it in plaintext.
- A user authenticated with `Draft@1234` may access only the forced password-change flow. Rate limits and lockouts protect the known shared temporary credential.
- **Retention and deletion policy (defined in PR-23, before production launch):** the authoritative policy is [`RETENTION_POLICY.md`](RETENTION_POLICY.md). Summary: profile data is minimal (name, email, optional avatar/team name) and accounts are deactivate-and-retain; draft history, results, and both append-only audit trails are retained indefinitely by design (§9.7, §9.10); the security-audit trail is kept ≥ 24 months and delivered/failed outbox rows ≤ 12 months (operator purges per [`RUNBOOK.md`](RUNBOOK.md)); email secrets are cleared at send and server logs retain 30 days; verified erasure requests are honoured within 30 days by deactivation plus pseudonymization of the profile identifiers, preserving the integrity of shared draft history. Backups follow the Neon point-in-time history window.

### 12.4 Scaffold completeness and interaction linkage

- The initial scaffold must implement functional routing, authentication guards, responsive navigation, API client wiring, and shared loading/error/empty states.
- Every visible sidebar item, bottom-navigation item, card link, button, and icon action must lead to its intended route, modal, drawer, or handler; no unexplained dead controls are permitted.
- Password fields use accessible eye icons with `Show password`/`Hide password` labels and preserved cursor/focus behavior.
- Controls that depend on later modules must be visibly marked `Coming soon` and disabled rather than appearing operational.
- Deep links, browser back/forward, refresh, and authenticated route restoration must work after the scaffold.
- Automated smoke tests cover sign-in, forced password change, navigation linkage, lobby creation, and protected route behavior.

## 13. Experience and visual direction

### 13.1 Design principles

1. **Broadcast energy, product clarity:** dramatic presentation surrounds the action but never obscures turn state or eligibility.
2. **The clock is sacred:** current team, active position, and remaining time are always visible.
3. **One-handed drafting:** the most important mobile actions live within thumb reach.
4. **Motion communicates state:** animation confirms picks, turn changes, readiness, and errors; it is not decoration alone.
5. **Rivalry without confusion:** every draft team has a persistent number, name, avatar pair, and accent treatment; colour is never the only identifier when a lobby contains up to 10 teams.

### 13.2 Recommended colour system

| Token | Colour | Use |
|---|---|---|
| Deep Black | `#0B0B0F` | Primary application background |
| Charcoal | `#16161C` | Cards, navigation, sheets, and inputs |
| Graphite | `#23232A` | Raised surfaces, borders, and selected neutrals |
| Neon Pink | `#FF006E` | Primary action, active turn, and focus accent |
| Hot Magenta | `#FF2095` | Live states, gradients, and highlight moments |
| Electric Violet | `#7820FF` | Secondary energy, special states, and team accents |
| Off White | `#F7F7FA` | Primary text and icons |
| Success Green | `#35D07F` | Ready, connected, and accepted states |
| Warning Gold | `#F5B942` | Timer warning and attention |
| Alert Red | `#FF4D5E` | Errors, destructive actions, and timer danger |

Neon Pink is a controlled action signal rather than a large reading surface. The pink-to-violet gradient is reserved for the brand, active progress, and celebratory moments. Multi-team identity uses labels, order numbers, names, and accessible accent treatments; it must never depend on colour alone. Text and control combinations must meet WCAG 2.2 AA contrast.

### 13.3 Typography and imagery

- Follow the supplied moodboard's bold, condensed athletic display direction for headings. Use the named `Champions` face only if appropriately licensed; use Barlow Condensed as the implementation fallback.
- Use Inter for body and interface copy, with tabular numerals for timers and rapidly changing statistics.
- Avoid unlicensed club crests, league marks, player photography, and game branding in production assets.
- Use stadium lights, pitch geometry, tactical lines, glossy cards, locker-room scenes, crowd energy, subtle grain, and dark premium materials to create football atmosphere.
- The canonical moodboard is stored at `fc-draft-web/docs/assets/fc-draft-asset-moodboard.png`; it is a directional reference, not a source from which production logos or icons should be cropped.

### 13.4 Motion language

- Lobby readiness: fast pulse and snap-to-slot.
- Pick accepted: card lift, short tunnel/floodlight reveal, then movement into the formation.
- Turn change: side accent sweeps across the timer and active roster slot.
- Timer warning: controlled pulse from 15 seconds; no constant screen shake.
- Reconnection: calm progress state followed by a short synchronized confirmation.
- Respect `prefers-reduced-motion`; no critical information may exist only in animation.
- Target smooth 60 fps transitions on current iPhones; use transforms/opacity and avoid expensive full-screen effects.

### 13.5 Design intelligence and continuity

- UI/UX work must use the Codex skill **`ui-ux-pro-max`**, sourced from [nextlevelbuilder/ui-ux-pro-max-skill](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill), as the project design-intelligence reference.
- The supplied [asset moodboard](fc-draft-web/docs/ASSET_MOODBOARD.md) is the canonical visual reference and takes precedence over generic skill-generated style, colour, and typography suggestions.
- Invoke the skill for new pages and components, changes to layout, colour, typography, motion, responsive behaviour, navigation, accessibility, interaction design, and UI quality reviews. Pure backend and infrastructure work does not require it.
- Recommendations must be reconciled with the confirmed product rules for The Draft Room and the football-broadcast direction in this document; the skill supports product decisions but does not override requirements.
- Before implementing a new page or major visual direction, use the skill to generate or consult the project design system. Persist the approved system under `fc-draft-web/design-system/fc-draft-collaborator/MASTER.md`, with page-specific exceptions under `pages/`, so future sessions share the same source of truth.
- UI implementation must apply the detected React/Vite/Tailwind stack guidance and prioritize accessibility, touch interaction, performance, responsive layout, typography/colour, meaningful motion, forms/feedback, and navigation in that order where requirements do not dictate otherwise.
- Before UI delivery, use the skill's pre-delivery checklist to verify WCAG 2.2 AA contrast, keyboard and focus behaviour, 44×44 px touch targets, reduced-motion support, responsive layouts, safe areas, labelled icon controls, and the absence of unexplained dead interactions.

## 14. Non-functional requirements

| Category | MVP target |
|---|---|
| Responsive support | iPhone Safari/PWA from 375 px width; current Chrome, Safari, Edge, Firefox on desktop |
| Initial load | Core shell usable within 3 seconds on a typical 4G connection after compression/caching |
| Live propagation | Accepted pick visible to connected clients within 500 ms at p95, excluding client network failure |
| API performance | Standard authenticated reads under 500 ms at p95 |
| Availability | 99.5% monthly target for initial private release |
| Accessibility | WCAG 2.2 AA for core journeys; keyboard-operable desktop flow |
| Recovery | Reconnect to authoritative draft state without duplicate picks |
| Auditability | 100% of accepted picks and admin recovery actions have immutable events |
| Compatibility | No core action depends on hover, precise pointer input, or animation |

## 15. Analytics and success metrics

### 15.1 North-star metric

**Completed drafts per active group per month.**

### 15.2 Supporting metrics

- Invite-to-activation conversion.
- Lobby-to-draft-start conversion.
- Draft completion rate.
- Median time from lobby creation to first pick.
- Median pick time and timer-expiry rate.
- Reconnection success rate.
- Percentage of drafts requiring admin intervention.
- Monthly active players and repeat drafters.
- PWA installation rate.
- Transactional email delivery and click-through rates.

Analytics must not capture passwords, private tokens, or unnecessary email/body content.

**Instrumented in PR-23** behind the vendor-neutral `IProductAnalytics` seam on the
`FcDraft.DraftRoom` meter (exportable via any OpenTelemetry listener; no vendor SDK):
invite-to-activation, lobby-to-start conversion, completion outcomes, time-to-first-pick,
pick/turn durations with the timer-expiry tag, reconnection joins, admin-vs-host interventions,
and email delivery outcomes — carrying only low-cardinality tags (format/outcome/action; a unit
test whitelists the tag keys so nothing personal or secret can reach a measurement). PWA
installation rate and email click-through are recorded as deliberately deferred (client-only /
Brevo-side); monthly-active and repeat-drafter counts derive from the database. See
[`fc-draft-web/docs/PR23_EVIDENCE.md`](fc-draft-web/docs/PR23_EVIDENCE.md).

## 16. MVP acceptance criteria

The MVP is releasable when:

1. An admin can create an account with temporary password `Draft@1234`, trigger a working invite email, and the user is forced to choose a new password before entering the app.
2. Password fields have working accessible eye controls, and all visible scaffold navigation/actions are linked or explicitly disabled as `Coming soon`.
3. A host can run a 1v1 lobby with 2–10 participants and a 2v2 lobby with 4–16 participants; capacity and even-team rules are enforced.
4. A 2v2 host can assign lobby seeds and form teams containing exactly one Seed 1 and one Seed 2 player.
5. Only the host can start a ready lobby, and the server commits a random spinner order for every formed team.
6. In spinner order, teams can choose from five-star and 4.5-star Kick Off clubs and protect one eligible player.
7. Position drafting starts `ST → LW → RW`, gives every team 120 seconds per position, and shows only matching men's base players rated 75+.
8. Every eligible card exposes stats, alternate positions, `+`/`++` roles, and PlayStyles; Icons, Heroes, special cards, and women's players never enter the pool.
9. Every connected client consistently sees lobby presence, seeds, teams, spinner results, club/protected-player choices, picks, active team, position, timer, and squads in real time.
10. The server prevents invalid capacity/team formation, duplicate or ineligible footballers, out-of-turn picks, and stale concurrent commands.
11. A disconnected participant can reconnect and continue from authoritative state, and host/admin recovery actions have a complete audit trail.
12. The app is installable as a PWA; core journeys work at 375 px without horizontal scrolling and pass keyboard, reduced-motion, contrast, and touch-target checks.
13. Brevo handles invitations, password resets, draft invitations, and admin announcements through a reliable outbox.

## 17. Session and pull-request delivery plan

### 17.1 Operating rules

From v0.8 onward, implementation proceeds as **one mergeable vertical slice per working session and pull request**. The next session starts from the first unchecked PR below unless a production defect or explicit product decision changes the order.

Every PR must:

1. Have one primary outcome and avoid unrelated refactors.
2. Include the domain/API/UI work needed to make that slice usable; backend-only foundation PRs are explicitly identified.
3. Preserve Clean Architecture boundaries and keep controllers thin.
4. Add or update automated tests for every changed business rule and authorization boundary.
5. Include migrations when persistent data changes; never require manual schema edits.
6. Keep both `dotnet build src/FcDraft.API/FcDraft.API.csproj` and `npm run build` green.
7. Keep incomplete controls disabled and labelled `Coming soon`; no visible dead controls may be introduced.
8. Update this checklist, the README, and API documentation when behavior or setup changes.
9. For UI-bearing PRs, use `ui-ux-pro-max`, the canonical moodboard, and the persisted design system; verify keyboard behavior, visible focus, 44×44 px touch targets, reduced motion, light/dark contrast, safe areas, and 375 px layout.
10. End with a short verification record in the PR description: commands run, manual paths tested, migrations added, and known follow-ups.

Status markers:

- `[x]` complete and verified in the repository.
- `[ ]` not started or not yet acceptance-complete.
- A partially implemented feature remains unchecked; its completed foundation is described in the PR entry.

### 17.2 Baseline

#### [x] PR-00 — Runnable foundation

**Outcome:** Establish the current .NET 8 API and React PWA foundation.

**Included:** JWT login, forced first-password change, role guards, responsive shell, persistent light/dark theme, accessible password visibility, PWA manifest/service worker, static FC 26 player explorer, full in-memory admin user-management CRUD (create/invite, read/paginate, edit/update, delete), live Brevo invitation delivery, and basic in-memory room creation with the admin activity stream.

**Known limitation:** Identity, rooms, and activity are in-memory and reset on API restart; room creation does not yet create a functional participant lobby; the player snapshot is client-side; automated project tests are not yet present. The account directory still offers hard delete alongside deactivation; the deactivate-and-retain-only lifecycle, database-side pagination, and historical retention arrive with SQL persistence in PR-03/PR-04.

**Resolved follow-up (v0.10):** The Brevo API secret is no longer committed — the checked-in `appsettings.json` leaves `Brevo:ApiKey`/`Brevo:SenderEmail` blank, and the secret is supplied via gitignored `appsettings.Development.json` or `Brevo__*` environment variables.

**Resolved follow-up (v0.11):** User deactivation (`AccountStatus`) is now enforced in the in-memory foundation — admins can activate/deactivate accounts, and deactivated users are rejected at sign-in and when creating/joining draft rooms. Durable persistence of this state remains PR-03/PR-04.

### 17.3 Product and quality gates

#### [x] PR-01 — Lock MVP draft rules and data source

**Outcome:** Resolve all decisions in §19 before the persistent draft model is committed.

**Scope:** Add a final rules matrix and architecture decision records covering formation/position order, protected-player slot behavior, club and footballer uniqueness, straight versus snake order, 2v2 pick authority, timer expiry, host permission, substitutes/flexible slots, shortlist scope, odd 1v1 byes, and the authoritative/licensed FC 26 data source.

**Done when:** §5 and §19 contain no unresolved rule that changes the draft state machine or database constraints; each answer has an explicit acceptance example.

**Delivered (14 July 2026):** [`DRAFT_RULES.md`](DRAFT_RULES.md) locks all twelve decisions with a squad-shape/roster template, decision matrix, acceptance examples for 1v1, 2v2, held-player, uniqueness, snake ordering, timer expiry, and odd byes, plus derived domain/database constraints for PR-10. §5 assumptions are now confirmed rules, §6.3/§6.4 reflect the snake order and auto-pick expiry, and §19 records each resolution. Squad shape is 16 (1 held + 11-player 4-3-3 + 4 flexible subs); expiry auto-picks the best available eligible footballer; the EA public feed is authoritative with roles supplemented from secondary sources and media deferred.

#### [x] PR-02 — Add automated test and CI foundations

**Outcome:** Make every later slice independently verifiable.

**Scope:** Add .NET unit/integration test projects, frontend component tests, Playwright smoke-test scaffolding, deterministic test identities/data, and a CI workflow for restore, build, test, and frontend production build.

**Done when:** CI covers login, forced password change, protected routes, navigation linkage, and basic room creation; tests do not depend on live Brevo or external FC services.

**Delivered (14 July 2026):** Added `FcDraft.sln`, `tests/FcDraft.UnitTests` and `tests/FcDraft.Api.IntegrationTests` (.NET), a Vitest + Testing Library component suite and a Playwright smoke scaffold under `fc-draft-web/`, and a three-job GitHub Actions workflow ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) for backend restore/build/test, frontend `npm ci`/Vitest/production build, and Playwright e2e. Coverage maps to the definition of done: **login** (handler unit test, integration `200`/`401`, `LoginPage` component test), **forced password change** (integration invite → change → re-login using the fake-sender-captured one-time password, and the `RequireAuth` guard test), **protected routes** (integration `401` unauthenticated / `403` player-on-admin, `RequireAuth`/`RequireAdmin` component tests), **navigation linkage** (route-guard redirects, `LoginPage` routing to `/` vs `/change-password`, and the Playwright anonymous → `/login` redirect), and **basic room creation** (integration create/list/validation/auth). Deterministic identities reuse the seeded Development accounts, and the Brevo sender is faked, so no test depends on live Brevo or an external FC service. Verification: `dotnet test FcDraft.sln -c Release` → 45 passing; `npm run test:run` → 14 passing; `npm run test:e2e` → 3 passing; both production builds green.

### 17.4 Persistent platform and accounts

#### [x] PR-03 — PostgreSQL and EF Core persistence foundation

**Outcome:** Replace process-memory storage with a production database foundation.

**Scope:** Add the EF Core PostgreSQL context, explicit snake-case mappings, migration tooling, local configuration, database health check, transaction abstraction, and initial tables for users and platform metadata.

**Done when:** A clean database can be created exclusively from migrations; restart persistence and health behavior are integration-tested; no secret is committed.

**Delivered (15 July 2026):** EF Core `FcDraftDbContext` (PostgreSQL via Npgsql) with explicit snake-case `users` and `platform_metadata` tables and a unique normalized-email index; an `InitialCreate` migration that owns the entire schema (no `EnsureCreated`/manual DDL); a startup `IDatabaseInitializer` that applies pending migrations and idempotently seeds platform metadata plus the deterministic dev accounts; `EfIdentityService` behind the unchanged `IIdentityService`; an `ITransactionRunner` abstraction (`EfTransactionRunner`); and a `database` health check wired into `/health`. Persistence is opt-in via `ConnectionStrings:DraftRoom` — blank keeps the in-memory foundation so a fresh clone and the hermetic suite run without a database. The secret stays in gitignored config/env; the committed `appsettings.json` is blank. `tests/FcDraft.Api.DatabaseTests` (Testcontainers PostgreSQL) proves migration-created schema, restart persistence of users and passwords, `/health` database reporting, and transaction commit/rollback; it skips when Docker is absent and runs in CI, and a Docker-free test covers the unhealthy path. The engine was changed from the originally-planned SQL Server to PostgreSQL (see the update note at the top); the durable directory behaviors — DB-side pagination, historical retention, removal of hard delete, avatar/preferred team name — remain **PR-04**.

#### [x] PR-04 — Persistent user directory and account lifecycle

**Outcome:** Make administration durable and match the PRD lifecycle.

**Scope:** Move the full user directory onto the PostgreSQL store; preserve normalized unique email; support create, view, edit, activate, and deactivate; retain historical users instead of deleting them; add optional avatar/preferred team name; persist invitation and password-change state.

**Done when:** Deactivated users cannot sign in or join new drafts, historical attribution is retained, pagination executes in the database, and the delete action is removed.

**Delivered (15 July 2026):** DB-side search/paging/tallies via `SearchUsersAsync`, `FindByIdAsync` replacing full scans, hard delete removed (endpoint + UI + interface), optional avatar/preferred-team-name persisted, and the `AddUserProfileFields` migration. Verified with real-PostgreSQL tests (paging, retention, profile persistence across restart). See the v0.16 note above.

#### [x] PR-05 — Authentication security and session revocation

**Outcome:** Complete the MVP security boundary.

**Scope:** Resolve the fixed `Draft@1234` versus unique invite-secret decision from PR-01; implement the approved password hasher, failed-login rate limits, temporary lockout, forgot/reset password tokens, password change from Profile, logout-all-sessions, security stamps/token revocation, and security audit events.

**Done when:** A must-change-password account cannot access any other authenticated API; password change/reset, deactivation, and admin security actions revoke older tokens; authorization and rate-limit tests pass.

**Delivered (15 July 2026):** Decision resolved to a **unique one-time secret** (§5.1). BCrypt hashing with legacy-hash verification; `ForcedPasswordChangeMiddleware` (403 for a must-change token on any other endpoint); per-request security-stamp validation in `JwtBearerEvents` so change/reset/deactivate/logout-all revoke older tokens; `LoginThrottle` (5 failures → 15-min lockout, 429); single-use SHA-256 reset tokens with forgot/reset endpoints; `/api/auth/logout-all`; and an append-only security-audit trail. Frontend: forgot/reset pages, Profile security (change password + sign-out-everywhere), and 401 auto-logout. `AddAuthSecurity` migration. Verified with unit + integration + real-PostgreSQL tests. See the v0.16 note above.

#### [x] PR-06 — Durable Brevo email outbox

**Outcome:** Ensure account transactions survive Brevo outages.

**Scope:** Add `EmailOutbox`, background delivery worker, retry/backoff, idempotency, delivery metadata, invitation and password-reset templates, configuration validation, and fake sender support for tests.

**Done when:** User creation commits even when Brevo is unavailable, queued work retries safely, secrets remain server-only, and invitation/reset delivery is observable without exposing message secrets.

**Delivered (15 July 2026):** `EmailOutboxMessage` + `OutboxEmailQueue` (enqueue in the account transaction), `EmailOutboxProcessor` (exponential-backoff retry, secret cleared after send), `EmailOutboxWorker` background loop, and `GET /api/admin/email-outbox` (status without secrets). In-memory mode keeps inline delivery via `DirectEmailQueue`. `AddEmailOutbox` migration. A real-PostgreSQL test proves commit-during-outage → retry → delivery → secret cleared → observable. See the v0.16 note above.

### 17.5 Dataset and draft configuration

#### [x] PR-07 — Versioned footballer and club import

**Outcome:** Move the FC 26 dataset behind a validated server-owned import boundary.

**Scope:** Add dataset version, footballer, club, role, PlayStyle, position, and import-report persistence; validate duplicates, missing IDs, invalid positions, malformed rows, excluded content, 75+ eligibility, and five-star club data; document data rights and attribution.

**Done when:** An admin can validate an import without activation, activate a valid version, inspect errors, and retain prior versions; all required fields in §9.3 are stored or explicitly blocked by the approved source decision.

**Delivered (15 July 2026):** `PlayerDatasetVersion`, `Footballer` (+ normalized `FootballerPosition`; stats/roles/PlayStyles as jsonb), `Club`, and `DatasetImportIssue` tables; `EfDatasetAdminService` validates (duplicate/missing id, missing name, invalid position, <75 warning, missing club) and imports as a **draft**, then activation archives the previous active version and is blocked when errors exist. Admin endpoints for import-bundled/upload/list/detail/activate; the bundled FC 26 dataset seeds a fresh DB. Club five-star ratings are absent from the EA feed and curated in PR-09 (documented). `AddPlayerDataset` migration; real-PostgreSQL tests. See the v0.16 note above.

#### [x] PR-08 — Server-backed Player Explorer

**Outcome:** Make player browsing use the authoritative dataset API.

**Scope:** Add paged search with prefix/approved typo tolerance, filters for position/rating/club/league/nation, name/overall sorting, player detail queries, shared loading/error/empty states, and migration of the existing explorer away from the static JSON query path.

**Done when:** The UI exposes all available stats, alternate positions, roles, PlayStyles, club, league, and nation from the active dataset and query-boundary tests prove excluded content never appears.

**Delivered (15 July 2026):** `IPlayerQueryService` (EF over the active version + in-memory over the bundled snapshot) with DB-side prefix/substring search, position/rating/club/league/nation filters, and name/overall sort; `GET /api/players`, `/players/filters`, `/players/{externalId}`. The explorer UI now consumes the API (static JSON loader removed) with server-side pagination and league/nation filters. A real-PostgreSQL query-boundary test proves below-75, non-Kick-Off, and non-active-version content never appears. See the v0.16 note above.

#### [x] PR-09 — Roster templates and eligible clubs

**Outcome:** Establish configurable, versioned draft rules before lobbies use them.

**Scope:** Persist ordered roster templates, slot eligibility rules, default 120-second timer, active/inactive template state, eligible five-star Kick Off clubs, and admin template management.

**Done when:** A template snapshots its ordered positions into a draft, changes cannot alter an in-progress draft, and only eligible clubs/players from the pinned dataset version are returned.

**Delivered (15 July 2026):** `RosterTemplate` + ordered `RosterSlot` (Held / StartingPosition / FlexBench) with the 120s timer and active/inactive state; the locked 4-3-3 default is seeded. `EfRosterTemplateService` (list/detail/active/create/activate) and `EfClubDirectoryService` curate eligible five-star clubs from the **active** dataset. Admin Templates page (nav + route) shows the active template's ordered slots and the five-star club picker. The active template is the immutable snapshot source a draft freezes at start — the draft-side snapshot lands with the draft aggregate in **PR-10**. `AddRosterTemplates` migration; real-PostgreSQL tests. See the v0.16 note above.

### 17.6 Lobby and team formation

#### [x] PR-10 — Persistent draft aggregate and append-only event history

**Outcome:** Create the authoritative draft lifecycle foundation.

**Scope:** Add Draft, DraftParticipant, DraftTeam, DraftTeamMember, DraftRosterSlot, DraftEvent, status transitions, dataset/template snapshots, optimistic versioning, and audited command handlers.

**Done when:** Allowed transitions match §10, invalid transitions fail without partial writes, every accepted transition appends an immutable event, and current state can be rebuilt or verified from history.

**Delivered:** The `Draft` aggregate + `DraftParticipant`/`DraftTeam`/`DraftTeamMember`/`DraftRosterSlot`/`DraftEvent` entities; a pure `DraftStateMachine` enforcing the §10.1 table and `DraftStateProjection.Replay` for rebuild-from-history; a `Draft.Version` bumped per accepted transition with each move appending one immutable, sequence-numbered `DraftEvent`; two-layer optimistic concurrency (last-seen `ExpectedVersion` → 409, plus the `version` EF concurrency token and the unique `(draft_id, sequence)` index so one writer wins a race); configuration frozen at start (active template slots copied into `DraftRosterSlot`, pick timer snapshotted, active dataset version pinned); and audited `CreateDraft`/`TransitionDraft`/`StartDraft` handlers (+ `GetDraft`/`ListDrafts`) wrapping read → validate → mutate → append in `ITransactionRunner` so a rejected move leaves no partial write. Opt-in persistence via `IDraftStore` (`EfDraftStore` / `InMemoryDraftStore` + `InMemoryTransactionRunner`); migration `AddDraftAggregate`. One event beyond §10.2, `DraftAbandoned`, records the §10.1 `→ Abandoned` transition. Backend-foundation only — no new endpoints/UI (PR-11+). Proven by 62 new tests (state-machine matrix, aggregate, handlers, and PostgreSQL transitions/409/no-partial-write/lost-update/rebuild); `dotnet test FcDraft.sln` → 161 passing.

#### [x] PR-11 — Lobby creation, invitations, and attendance

**Outcome:** Turn room creation into a usable 1v1/2v2 lobby.

**Scope:** Select a name, format, roster template, and expected participants; make the creator host; add join/presence states, invite/remove/replace actions, lobby detail route, and capacity/even-count validation.

**Done when:** 1v1 enforces 2–10 and 2v2 enforces 4–16 even participants server-side; deactivated users are rejected; all participants can reopen the authoritative lobby snapshot.

**Delivered (15 July 2026):** `CreateDraftCommand` now creates the lobby, adds the creator as a joined host `DraftParticipant`, opens it (Draft → Lobby), binds a host-chosen roster template (or the active one), and seeds an initial invite list in one transaction. Audited, version-checked `InviteParticipant`/`JoinDraft`/`RemoveParticipant`/`LockLobby` commands drive attendance, each appending one `DraftEvent`. Capacity is enforced server-side — the maximum on create/invite (1v1 ≤ 10, 2v2 ≤ 16) and the minimum + even-count at lock (1v1 2–10, 2v2 4–16 even) — and deactivated accounts are rejected at host/invite/join. A new authenticated `DraftsController` exposes create, the participant/host/admin-only lobby snapshot (`GetDraft`, enriched with participant identities + a `LobbyCapacity` block), a caller-scoped list, an invitable-users directory, roster templates, and invite/join/remove/lock. `StartDraft` snapshots the draft's bound template. The legacy `/api/draft-rooms` stub + `IDraftRoomService` were retired and the frontend migrated onto the new **New lobby** flow (format/template/invite), a `/drafts/:id` **lobby detail** route with per-slot invite/join state and host controls, and a per-user draft hub. Two string-persisted events (`ParticipantRemoved`, `LobbyLocked`) were added for §10.1 actions §10.2 did not name — **no migration required** (the PR-10 `DraftParticipant`/`DraftEvent` tables are now populated). Proven by 29 new tests (unit capacity/host-only/join/remove; integration create/snapshot/invite/join/remove/lock/auth; and PostgreSQL persistence, capacity enforcement, and deactivated-user rejection); `dotnet test FcDraft.sln` → 190 passing (124 unit, 38 hermetic integration, 28 PostgreSQL); `npm run test:run` → 21 passing.

#### [x] PR-12 — 2v2 seeding, team formation, and ready check

**Outcome:** Make both formats start-ready under the confirmed rules.

**Scope:** Add host-only Seed 1/Seed 2 assignment, solo-team projection for 1v1, paired-team formation for 2v2, ready/unready actions, attendance confirmation, validation summaries, and configuration freeze rules.

**Done when:** Every 2v2 team has exactly one Seed 1 and one Seed 2; only the host can change formation; Start remains disabled until attendance, assignments, and readiness pass; changes update the draft version and event history.

**Delivered (15 July 2026):** Host-only `AssignSeedCommand` (2v2, team-formation state only) sets `DraftParticipant.Seed`; `FormTeamsCommand` replaces the team layout — 1v1 auto-projects one solo `DraftTeam` per participant, 2v2 pairs participants into teams each validated as exactly one Seed 1 + one Seed 2 with a participant on at most one team; self-service `SetReadyCommand` toggles `IsReady`; and host-only `BeginReadyCheckCommand` (TeamFormation → ReadyCheck, gated on everyone present + assigned to valid teams) / `ReopenTeamFormationCommand` (ReadyCheck → TeamFormation, clearing readiness) drive the ready check. Re-forming teams clears readiness. `StartDraftCommand` is now gated by the §9.4 rule (all present, all assigned, every team valid, all ready) via the shared pure `DraftFormation.Evaluate`, which also feeds a `StartRequirements` block on `DraftDetail` so the client renders the same rules. Each mutation bumps `Draft.Version` and appends one `DraftEvent`; two string-persisted events (`ReadyCheckStarted`, `TeamFormationReopened`) record the §10.1 transitions §10.2 did not name — **no migration required**. `DraftsController` gains `seeds`/`teams`/`ready`/`ready-check`/`reopen-teams`/`start`. Frontend: a team-formation stage (2v2 seed toggles + pair builder, 1v1 solo confirm, validation summary) and a ready-check stage (ready toggle, reopen, gated Start). Proven by new unit, integration, and PostgreSQL tests (seed validity, team rule, host-only, the Start gate).

#### [x] PR-13 — Server-authoritative spinner ranking

**Outcome:** Commit and reveal a fair, durable team order.

**Scope:** Add host-only start authorization, unbiased server shuffle, unique rank constraints, committed/revealed events, deterministic test seams, animated reveal UI, and reduced-motion equivalent.

**Done when:** The visual wheel cannot influence the result, all teams receive one unique stored rank, retries cannot reshuffle a committed result, and non-host commands are rejected.

**Delivered (15 July 2026):** Host-only `CommitSpinnerCommand` (SpinnerRanking state) draws an unbiased **Fisher–Yates** order through an injected `IShuffler` seam (mirrors the `TimeProvider` pattern — `FisherYatesShuffler` over `Random.Shared` in production, a deterministic shuffler in tests), assigns each team a unique `SpinnerRank` (enforced by the existing `(draft_id, spinner_rank)` unique index), and appends `SpinnerOrderCommitted` + `SpinnerOrderRevealed`. It is **idempotent** — a committed order is never reshuffled by a retry (and a stale version → 409). The `/spinner` endpoint and a frontend spinner stage — an animated wheel that reveals the server-committed order with a reduced-motion-safe ordered list and a host **Spin** control — complete it; the wheel is decorative and cannot influence the result. The SpinnerRanking → ClubSelection transition is PR-14 (club selection stays "Coming soon"). Proven by new unit, integration, and PostgreSQL tests (rank uniqueness, idempotency, determinism under an injected shuffle, non-host rejection) plus a direct Fisher–Yates permutation test.

### 17.7 Club selection and live draft engine

#### [x] PR-14 — Five-star club and protected-player round

**Outcome:** Complete the pre-draft selection round in spinner order.

**Scope:** Add ordered club turns, club eligibility/uniqueness rules, club details, protected-player eligibility, slot linkage under the PR-01 decision, availability updates, and transition to position drafting.

**Done when:** Each team completes one valid club/protected-player choice, stale or duplicate choices are rejected transactionally, and the first position cannot begin until every protected player is locked.

**Delivered (16 July 2026):** Host-only `OpenClubSelectionCommand` opens the round (SpinnerRanking → ClubSelection; new string event `ClubSelectionStarted`, no migration for the event). `SelectClubAndProtectCommand` runs in **straight spinner order** (pure `DraftTurnOrder.Straight` over committed ranks): the active team — either teammate (first valid wins) or an admin — chooses one **five-star club** (unique per lobby, enforced by the guard and a unique `(draft_id, selected_club_id)` index) and protects one **75+ Kick Off footballer from that club** (globally removed from every pool via a unique `(draft_id, footballer_id)` index), appending `ClubSelected` + `FootballerProtected` at one version and recording the held pick at slot Order 0. Host-only `OpenPositionDraftCommand` (ClubSelection → PositionDraft, `PositionRoundStarted`) is **gated until every team has locked its club and protected player**. Eligibility reads go through a new `IDraftCatalog` seam scoped strictly to `Draft.PinnedDatasetVersionId`. A `board` read endpoint surfaces the active team and the available clubs / held pool; the frontend adds a poll-based club-selection stage. Proven by unit, integration, and PostgreSQL tests (straight order, club/footballer uniqueness firing transactionally, club-match + 75+, out-of-turn/stale/ineligible rejection, the position-draft gate).

#### [x] PR-15 — Position draft state machine and pick validation

**Outcome:** Support authoritative position-by-position drafting.

**Scope:** Start at `ST → LW → RW`, advance through the approved template, enforce turn/team/position/rating/dataset/availability rules, accept the first valid teammate submission, add idempotency and draft-version conflicts, and append accepted-pick events.

**Done when:** Duplicate, stale, out-of-turn, ineligible, and wrong-state picks are rejected; one footballer/slot/turn can win only once; a complete final slot transitions the draft to Completed.

**Delivered (16 July 2026):** Host-agnostic `SubmitPickCommand` advances the frozen `DraftRosterSlot` snapshot ST → LW → RW → CM×3 → LB → CB×2 → RB → GK then 4 flexible subs, in **snake order** over committed spinner ranks (`DraftTurnOrder.NextPosition`). Either teammate of the active team (or an admin) may submit; the **first valid server-accepted** submission wins and closes the slot. Each pick enforces turn + team authority, position eligibility (primary/alt matches the slot; a flexible bench slot accepts any), 75+ rating, pinned-dataset membership, and availability, carries `ExpectedVersion`, and appends one `PickAccepted`; duplicate/stale/out-of-turn/ineligible/wrong-state picks are rejected (guards + the unique `(draft_id, footballer_id)` and `(draft_team_id, slot_order)` indexes). Filling the final slot transitions **PositionDraft → Completed** (`DraftCompleted`) in the same transaction. Migration `AddDraftPicks` adds the shared `draft_picks` table and indexes (forward-safe; runs on the prod deploy). The frontend adds poll-based position-draft and completed (final-squad) stages. Proven by unit, integration, and PostgreSQL tests (snake order across all rounds, one-footballer/slot uniqueness, 2v2 either-teammate authority, out-of-turn/duplicate/ineligible/stale rejection, and completion → Completed). The 120s timer and auto-pick remain **PR-16**.

#### [x] PR-16 — Server timer and host controls

**Outcome:** Make time and exceptional draft states authoritative.

**Scope:** Add the 120-second server clock, 15-second warning, approved expiry behavior, host pause/resume/cancel with reason, admin recovery authorization, timer restoration, and compensating audit events.

**Done when:** Refresh/restart-safe state computes the same remaining time, paused time does not elapse, unauthorized controls fail, and cancellation/recovery never deletes original history.

**Delivered (16 July 2026):** The 120s pick clock is persisted state (nullable `turn_started_at`/`paused_at`, migration `AddDraftTimer`): the anchor is set when the position draft opens and after every accepted pick, so remaining time = `turn_started_at + 120s − now` on any server or client — restart/refresh-safe by construction; the club/held round is untimed (§6.4 times "each position"). Expiry is enforced **lazily** in the board/get/pick/pause paths plus a hosted 5s sweep while the instance is warm (scale-to-zero safe); the auto-pick is the deterministic §6.4 best (highest overall → name → id) accepted through the same shared `PickEngine` path as a human pick, recorded as the new string event `PickAutoSelected` (null actor, audited reason, no migration), the next turn anchored at the expired deadline so cascaded catch-ups stay exact, the final slot completing the draft. Concurrent expiry triggers yield exactly one pick (version token + unique indexes — proven against real PostgreSQL). `DraftTimerDto` (deadline/remaining/paused/15s warning) rides `DraftDetail` and the board. Host-or-admin pause/resume/cancel carry a required 512-max audited reason: pause freezes the clock (paused time never elapses — a paused draft never auto-picks), resume returns to the recorded pre-pause state shifting the anchor by the pause duration, cancel preserves the append-only history. Admin-only `ApplyAdminRecovery` (§9.7) appends a compensating `AdminRecoveryApplied` event with optional turn-clock restore; original history is never edited or deleted.

#### [x] PR-17 — SignalR synchronization and reconnection

**Outcome:** Synchronize the entire lobby and draft across clients.

**Scope:** Add authenticated draft hubs/groups, typed events for presence, seeds, teams, readiness, spinner, clubs, protected players, timer, picks, squads, and controls; add reconnect snapshot/version reconciliation and connection-status UI primitives.

**Done when:** Multi-client integration tests show accepted state within the live propagation target, reconnect restores an authoritative snapshot without duplicated actions, and clients refresh cleanly after a version conflict.

**Delivered (16 July 2026):** An authenticated `/hubs/draft` SignalR hub (JWT over the websocket via the `access_token` query string through `JwtBearerEvents.OnMessageReceived`, hub paths only) with one group per draft id; joining is authorized exactly like `GET /drafts/{id}` (participant/host/admin; 404-equivalent rejection so a lobby's existence is not leaked) and returns the authoritative snapshot so a (re)connect reconciles state and version in one round-trip. Every accepted mutation — presence, seeds, teams, readiness, spinner, clubs/protected players, picks, timer auto-picks, and pause/resume/cancel/recovery — broadcasts one version-stamped `DraftUpdated` envelope `{draftId, version, eventType, detail}` published AFTER the transaction commits through a new `IDraftNotifier` seam (no-op default in Application, SignalR-backed in the API via a MediatR post-handler behavior; the expiry service publishes its own auto-picks); a null `detail` means "refetch over REST". Clients use `withAutomaticReconnect`, rejoin the group on reconnect, apply snapshots only forward by version, and keep the REST reads as the fallback — SignalR augments them. Single-instance Cloud Run keeps SignalR in-process (no backplane) with 15s keep-alives inside the request timeout. Frontend: `@microsoft/signalr`, a connecting/reconnecting/offline status banner, live snapshot application, and a server-calibrated live countdown (15s warning) driven by the pushed deadline. Multi-client integration tests prove fan-out to two clients within the §14 **500 ms** propagation target, unauthorized-join rejection, reconnect reconciliation with no duplicated actions, and clean refresh after a 409. The rich draft-room UX remains PR-18.

#### [x] PR-18 — Live draft room experience

**Outcome:** Deliver the complete desktop and one-handed iPhone drafting workflow.

**Scope:** Build active team/position/timer header, eligible player search and filters, player detail, confirmation sheet, shortlist, compact team rail, focused squad, recent picks, full history, sticky mobile action area, connection/reconnect states, and pick/turn motion.

**Done when:** A participant can complete every pick without leaving the room at 375 px; all actions work by keyboard and touch; critical state is explicit without animation; unavailable players and stale-command recovery are understandable.

**Delivered (16 July 2026):** The draft room renders only server state — the board plus pushed `DraftUpdated` snapshots; the client never derives eligibility or turn order. Header: active team · slot, round + "Pick N of M" progress, an explicit no-animation **"Your turn"** state, the server-calibrated countdown, and a live-connection indicator. Search/filter never leave the room or the pinned pool: `GET /drafts/{id}/board` gains `search` (wired through `CatalogFootballerFilter.Search`; `FakeDraftCatalog` honours it) and a deliberate `take` (default 100, "show more" pages toward the 500 cap — never `/api/players`, which reads the ACTIVE dataset). §9.6 detail cards read on demand from `GET /drafts/{id}/footballers/{footballerId}` — a new `CatalogFootballerCard` (stats, roles with `+`/`++`, PlayStyles as passthrough JSON, club/league/nation) on `IDraftCatalog` (Ef/InMemory/Fake aligned) plus this draft's availability, so an unavailable player is understandable (who holds it, in which slot). A confirmation sheet naming the draft team and roster slot guards every selection (no one-tap picks; protect uses the same sheet). The personal shortlist (decision 11) is **client-side localStorage keyed per user and per draft** — the recorded MVP persistence decision; it is a solo planning aid the server never trusts, and its entries are marked taken/ineligible from server state. The compact team rail offers focused-squad, recent-picks, and full-history views (chronology re-derived from the deterministic held-then-snake rules; display-only) — never all squads at once at 375 px. On iPhone the room and paused stage replace the global bottom navigation with a sticky action area (§8.3); 44×44 touch targets, keyboard-operable dialogs (focus trap + Escape), reduced-motion equivalents for pick/turn motion, and a 409 that resyncs automatically with a plain explanation. `LobbyPage` (~860 lines) was decomposed into per-stage components under `components/draft/`.

### 17.8 Completion and operations

#### [x] PR-19 — Results and squad archive

**Outcome:** Preserve and reopen completed drafts.

**Scope:** Create immutable result snapshots, formation/list squad views, average and line ratings, club/league/nation summaries, pick sequence, Draft Hub active/upcoming/completed views, Teams archive, and read-only completed-draft routes.

**Done when:** Later dataset/template changes cannot alter historical results and every participant/admin can reopen an authorized completed draft.

**Delivered (16 July 2026):** `GET /drafts/{id}/results` (404 until Completed; participant/host/admin-scoped like every draft read) serves per-team **average and GK/DEF/MID/FWD line ratings computed from the frozen picks and frozen slot positions**, represented clubs/leagues/nations, member names, and the global pick sequence. **Recorded decisions:** the sequence is re-derived from the deterministic turn rules (straight held round, then snake) — the exact acceptance order, preferred over `CreatedAt` whose sub-second ties are less deterministic; and **no dedicated snapshot table was added** (per §17.10.4) because the §9.7 immutability requirement is already satisfied by the existing frozen data — `draft_picks` denormalized name/overall/position at pick time and `draft_roster_slots` froze at start (PR-15), while the display-only club/league/nation extras resolve from the draft's PINNED dataset version (whose rows never change after import) via a new bulk `MapFootballerFactsAsync`, with the team's club name preferring the held pick's immutable facts (the five-star flag is admin-mutable). The §17.8 done-when is proven by a PostgreSQL test that completes a draft, imports AND activates a different dataset version, and re-reads **byte-identical** results; no backfill was needed for existing completed prod drafts because nothing new is written. Frontend: a read-only `/drafts/{id}/results` route with FORMATION (starting XI on a pitch from the frozen slot positions; held + bench below) and LIST views, rating chips, represented chips, and the numbered sequence; `CompletedStage` links to it; the Draft Hub groups **Live now / Upcoming / Completed / Ended early** (completed cards open results); the `/teams` placeholder became the squad archive (your squad's headline avg/club per completed draft).

#### [x] PR-20 — Player notifications and draft emails

**Outcome:** Complete essential participant communications.

**Scope:** Add persistent per-user notifications, unread badge, mark-read behavior, email preferences, and outbox-backed draft invitation, reminder, cancellation, and completion messages.

**Done when:** Notifications are authorization-scoped, survive restart, deep-link to the correct draft, and optional preferences never suppress mandatory security/service messages.

**Delivered (16 July 2026):** A persistent `user_notifications` table (forward-safe migration `AddUserNotificationsAndEmailPreferences`: one new table plus additive `users.optional_email_opt_out` and non-secret `email_outbox.payload` columns) holds per-user notifications (type, title/body, draft deep-link, read-at, created-at). A `DraftParticipantNotifier` appends notification rows **and** outbox emails INSIDE the mutating command's transaction — a rolled-back command never notifies — for: participant invited (creation + later invites), draft cancelled (with the recorded reason), draft completed (both a live final pick and the expiry sweep's auto-pick completion), and a **host-initiated reminder** (`POST /drafts/{id}/remind`, pre-start states only — the recorded MVP reminder trigger; no scheduler exists). New `/api/me/notifications` endpoints (list newest-first + unread count, mark-read, mark-all-read) are authorization-scoped — another user's notification id reads as 404 — and deliberately distinct from the admin-only `/api/notifications` SSE activity centre, which is unchanged. Emails go through the EXISTING durable outbox (never inline Brevo in a draft transaction): four new string-stored `EmailKind`s carry a JSON payload to a new `BrevoDraftEmailSender` (invitation/reminder/cancelled/completed-with-result-link; links derive from `Brevo:AppBaseUrl`, falling back to the already-configured LoginUrl origin — no new prod setting required); the in-memory foundation swallows and logs draft-email failures so a Brevo outage can never fail a draft mutation on either branch. **§9.9 recorded decision:** invitations, cancellations, and completion results are ESSENTIAL service messages about the recipient's own participation and always send; the reminder is the OPTIONAL announcement-style nudge — only its email honours the opt-out (enforced at enqueue; the in-app notification always lands), settable from Profile via `GET/PUT /api/me/email-preferences`. Frontend: a notification centre in the shell for every signed-in user (unread badge, mark-read/mark-all, deep links — completed notifications open the results page) and the host "Send reminder" control in the lobby. Proven: notifications survive an API restart (a fresh host over the same database) with persisted read stamps; a cancellation commits notification rows and outbox email rows together; a simulated Brevo outage never fails the cancelling mutation.

#### [x] PR-21 — Admin communications, draft operations, and audit views

**Outcome:** Complete MVP administration and recovery.

**Scope:** Add announcement audience/preview/confirmation, Brevo campaign metadata, draft inspect/pause/resume/cancel operations, immutable audit-log queries, reason capture, delivery visibility, and role-aware Admin versus Player context.

**Done when:** Bulk sends are confirmed and throttled, every admin/host action is attributable, audit records cannot be edited/deleted through normal APIs, and recovery uses compensating events.

**Delivered (16 July 2026):** The admin Communications module composes an announcement against a chosen audience (all active players, or one draft's active participants), PREVIEWS it (subject, sender, resolved audience count, §9.9 opt-out split) and sends only after explicit confirmation — the send re-resolves the audience and **409s when the count moved since the preview**, so a bulk send can never quietly address an audience the admin did not review. One transaction commits the append-only `announcements` campaign record (the §9.8 campaign metadata: audience definition, counts, requester, time), the matching in-app notifications through the PR-20 pipeline (opted-out players still get the notice — the announcement email is exactly the OPTIONAL class the PR-20 `optional_email_opt_out` preference exists for, enforced at enqueue), an `AnnouncementSent` audit record, and the campaign-stamped outbox emails — via the EXISTING PR-06 durable outbox as a string-stored `EmailKind.Announcement`, **throttled** by staggering `next_attempt_at` in 20-per-15-second windows so the worker drains a bulk audience steadily (the Brevo payload carries the campaign id as a tag). Delivery is visible per campaign (pending/sent/failed tallies) and per email (`GET /api/admin/email-outbox`), on BOTH storage branches — the in-memory foundation records inline outcomes in a new ledger. **Draft operations:** `/admin/drafts` became real operations — inspect any draft (state, version, participants, the full append-only event history) and pause/resume/cancel on the EXISTING PR-16 commands as an admin actor; pause/cancel require a reason, every action carries the last-seen version (stale → 409 + resync), and correction stays compensating: a PostgreSQL proof re-reads the pre-existing `draft_events` rows byte-for-byte identical after an admin pause/resume round-trip, the new events appended gap-free with the admin attributed. **Audit views:** admin-only, read-only queries over both append-only trails with the §17.8 filters (draft, user, type, action, email, date) and resolved actor names; the §9.10 admin actions (user create/update/invite/activate/deactivate, dataset import/activation, template create/activation, five-star curation, bulk announcements) are now recorded with the acting admin's id/email/IP; no update or delete verb exists on any audit surface. **Navigation:** Communications and Audit Log ship behind `RequireAdmin`; per §12.4 the intentionally deferred pieces are visible and disabled (“Coming soon”): the §8.2 Overview dashboard nav item and Brevo-TEMPLATED announcements (plain announcements ship). Also fixed a latent `useFocusTrap` re-initialization bug that stole focus mid-typing in any dialog form. Proven by 322 backend tests (213 unit, 67 hermetic, 42 PostgreSQL), 78 frontend tests, and 6 Playwright checks.

### 17.9 Release hardening

#### [x] PR-22 — PWA lifecycle, accessibility, performance, and observability

**Outcome:** Meet the non-functional release requirements.

**Scope:** Add offline/reconnecting behavior, block offline mutations, install and iOS guidance, service-worker update prompt/version handshake, safe-area/orientation/keyboard handling, structured logs, correlation IDs, metrics, error monitoring hooks, and performance/accessibility fixes.

**Done when:** Core journeys pass automated accessibility checks plus manual keyboard, reduced-motion, light/dark contrast, 44×44 px touch-target, 375 px, landscape, and safe-area reviews; authenticated API data is not cached; measured performance is recorded against §14.

**Delivered (16 July 2026):** The §18 "cached PWA shell becomes incompatible with API" risk is closed with a compiled-in **client/API contract** (`ApiContract.cs` ↔ `apiContract.ts`, a cross-reading test fails CI when they drift; bumped only on a breaking API change): every `/api` response stamps `X-DraftRoom-Contract` and the anonymous `GET /api/meta/version` reports `{contract, revision}` (Cloud Run's `K_REVISION`), the client compares on every axios response plus at boot/foreground, and a mismatch nudges the service worker and raises the refresh prompt — the same explicit banner the `virtual:pwa-register` `onNeedRefresh` flow shows when a new shell finishes downloading (checks run hourly and on returning to the foreground). Authenticated API data is **never cached**: `Cache-Control: no-store` is stamped on all `/api` responses (asserted for authenticated reads, the login response, and 401s), the workbox `navigateFallbackDenylist` covers `/api`, `/hubs`, `/health`, `/swagger` with `runtimeCaching` empty, and an e2e proof parses the generated `dist/sw.js` (no API URL precached, no runtime caching strategy exists). **Offline is a state, not an error:** a lifecycle store tracks connectivity, a toast-style live-region banner covers every journey (sign-in included), mutations are rejected at the single axios seam BEFORE anything is sent ("You're offline. Reconnect to continue — nothing was sent."), the pick-confirmation sheet disables its confirm with an inline explanation, and reads still pass; the PR-18 hub indicator keeps owning transport-level Live/Reconnecting. **Install guidance (§12.2):** offered only after the user has value — a dismissible card on the draft hub once drafts exist plus a permanent profile panel — using the captured Chromium `beforeinstallprompt` in-product, the spelled-out Safari **Share → Add to Home Screen** steps on iOS, and an "installed" state when standalone. **Ergonomics:** `env(safe-area-inset-*)` on every exposed edge (topbar, sidebar, bottom nav, draft action bar, page container, banners), `interactive-widget=resizes-content` plus a `--keyboard-inset` custom property fed by `visualViewport` so the draft room's sticky action area rides above the iOS on-screen keyboard, landscape short-viewport rules (compact sticky room header — the clock stays visible; dialogs scroll via `max-height: 100dvh`), and `100dvh` body sizing. **Observability (§12.1):** a correlation id per request (honouring well-formed `X-Correlation-Id`) echoed on the response header, held in an AsyncLocal accessor, and wrapped as a logging scope; a new OUTERMOST MediatR behavior emits one structured log line + metrics sample per request (name, duration, outcome, correlation id) — proven to carry the caller's id into the handler pipeline; Production logs are JSON console lines with scopes (Cloud Run-queryable); vendor-neutral `System.Diagnostics.Metrics` instruments (request duration/failures/unhandled errors, readable via dotnet-counters or any OTel exporter later); an `IErrorReporter` seam whose default logs + counts (swapping in Sentry etc. is one DI registration — no vendor lock) now receives all unexpected 5xx exceptions, and ProblemDetails responses carry `correlationId`; `/health` reports `contract`, `revision`, and a new `self` check on BOTH storage branches (the SQL branch keeps `database`). **Performance (§14, measured — see `fc-draft-web/docs/PR22_EVIDENCE.md`):** shell load **1.36 s** on emulated Slow 4G / **0.33 s** on typical 4G (budget ≤3 s; ~205 KB compressed first load after route-splitting results/archive/explorer and all admin modules out of the 96 KB-gzip entry chunk); authenticated reads p95 **1.6–2.6 ms** locally (budget <500 ms; live `/health` reference 155 ms TLS round-trip); accepted-mutation → connected-client SignalR visibility p95 **0.2 ms** after accept (budget ≤500 ms). **Accessibility:** axe automated checks over the core journeys — Playwright with real color-contrast in BOTH themes plus jsdom component scans of hub/room/profile — surfaced and fixed a prohibited `aria-label` on the brand `div` (now `role="img"`) and 12 px eyebrows in raw magenta at 3.6:1 (new `--color-secondary-text` AA ramp: 6.5:1 light / 7.7:1 dark); the one sub-44 px control (`banner-dismiss`, 36 px) gained a 44 px hit area; type floor verified at 12 px minimum; manual review matrix (keyboard, reduced-motion, contrast, 375 px, landscape, safe-area, offline) recorded with real-app screenshots in `docs/assets/pr22-evidence/`. **Recorded corrections:** the PR-21 note counted 78 frontend tests; the true baseline was 76. Verified: `dotnet test FcDraft.sln` → **336** (217 unit, 77 hermetic, 42 PostgreSQL), `npm run test:run` → **103**, `npm run test:e2e` → **14** (axe, offline, 375 px/landscape, keyboard, service-worker proofs), both production builds green. The next session is **PR-23 — end-to-end MVP verification and private-beta release**.

#### [x] PR-23 — End-to-end MVP verification and private-beta release

**Outcome:** Prove all 13 acceptance criteria in realistic sessions.

**Scope:** Add complete 1v1 and 2v2 multi-client E2E suites, concurrency/race tests, reconnect and Brevo-outage scenarios, deployment/runbook documentation, seed/demo data, backup/recovery notes, retention policy, analytics events, and a private-beta checklist.

**Done when:** Every item in §16 has linked automated or recorded manual evidence, repeated real-iPhone and desktop sessions complete successfully, no release-blocking defect remains, and the private-beta build is reproducible from a clean environment.

**Delivered (16 July 2026):** See the dated PR-23 note in the update history above for the full record. In brief: a full-stack Playwright harness (`npm run test:e2e:full`; API in environment Testing behind the production preview proxy) drives complete multi-client **1v1 and 2v2** drafts through the real UI to results, plus real-client **concurrency/race, reconnect, and Brevo-outage** scenarios; [`fc-draft-web/docs/PR23_EVIDENCE.md`](fc-draft-web/docs/PR23_EVIDENCE.md) links **all 13 §16 criteria** to automated tests and/or recorded manual evidence with the device-session kit in [`PR23_DEVICE_SESSIONS.md`](fc-draft-web/docs/PR23_DEVICE_SESSIONS.md) (operator-run sessions record there); §15 analytics ship behind the vendor-neutral `IProductAnalytics` seam with a proven privacy whitelist; the §12.3 **retention policy is defined** ([`RETENTION_POLICY.md`](RETENTION_POLICY.md)); [`RUNBOOK.md`](RUNBOOK.md) + a restructured [`DEPLOYMENT.md`](DEPLOYMENT.md) document the REAL Cloud Run + Neon + WIF path (Render demoted to a legacy appendix) with backup/PITR recovery and single-instance caveats; demo players seed behind `Database:SeedDemoAccounts` on both storage branches; [`PRIVATE_BETA_CHECKLIST.md`](PRIVATE_BETA_CHECKLIST.md) gates the launch; and the build is **proven reproducible from a clean clone** (341 backend / 103 vitest / production build / container build + boot; the container also builds in CI on every run). Baselines: 341 / 103 / 14 / 5, all green.

### 17.10 Progress update convention

At the end of every future session:

1. Change only the completed PR marker from `[ ]` to `[x]` after its full definition of done is met.
2. Add a dated one-line `PR-XX completed` note to the document update history at the top.
3. If a PR is split, insert the new PR immediately after it and renumber later unchecked PRs before implementation begins.
4. If product scope changes, update the governing requirement first, then adjust affected future PRs.
5. Never mark a PR complete solely because its code compiles; tests and acceptance evidence are part of completion.

## 18. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| FC 26 data, imagery, or trademarks are not licensed | Launch/legal risk | Confirm source and usage rights before asset integration; keep import adapter and branding replaceable |
| Draft rules remain ambiguous | Rework in state machine and UI | Lock decisions in Phase 0 and test with clickable prototypes |
| Two teammates submit simultaneously | Duplicate/conflicting pick | Server transaction, unique constraints, draft version check, idempotency key |
| Mobile network interruption | Missed turns and distrust | Server-authoritative timer/state, reconnect snapshot, pause policy |
| Brevo outage or throttling | Missing invitations/announcements | Durable outbox, retries with backoff, delivery visibility, rate controls |
| Heavy card art and animation hurt iPhone performance | Poor core experience | Responsive images, lazy loading, GPU-friendly motion, performance budgets, reduced-motion mode |
| Cached PWA shell becomes incompatible with API | Broken sessions after deploy | Version handshake, safe service-worker activation, refresh prompt |
| Admin bulk email is misused | Spam/reputation damage | Active-user audience only, preview/confirmation, permissions, audit log, throttling |
| Every new account shares the known temporary password `Draft@1234` | Account takeover before activation | Force password change before app access, rate-limit sign-in, lock out repeated failures, never expose participant emails in lobbies; prefer unique invite secrets in a later security revision |

## 19. Draft rules decisions (locked in PR-01)

All twelve decisions below are **resolved**. The authoritative matrix, roster
template, acceptance examples, and derived database constraints are in
[`DRAFT_RULES.md`](DRAFT_RULES.md); the resolutions are summarized here.

1. **Formation & sequence:** 4-3-3 — `ST → LW → RW → CM → CM → CM → LB → CB → CB → RB → GK`, then 4 flexible substitutes.
2. **Held player:** drafted from the chosen elite club into a separate 12th squad slot outside the XI; removed from every team's pool; it does not fill or skip a formation position.
3. **Club eligibility & uniqueness:** eligible clubs are FC 26 men's five-star or 4.5-star Kick Off clubs (updated 17 July 2026 from five-star only); each may be chosen by only one draft team per lobby.
4. **Footballer uniqueness:** globally unique — once held or drafted, a footballer is unavailable to all teams.
5. **Round order:** snake (reversing each position/bench round); the pre-draft club/held round is straight spinner order.
6. **2v2 pick authority:** either teammate may confirm; the first valid server-accepted submission wins.
7. **Timer expiry:** auto-pick the highest-rated available eligible footballer for the active slot (tie-break overall → name → id).
8. **Host permission:** any active (non-deactivated) user may create/host a lobby.
9. **Data source:** EA public FC 26 ratings feed is authoritative; Role/Role++ and PlayStyles supplemented from secondary sources; licensed media deferred until rights are confirmed.
10. **Substitutes:** 16-player squad — the XI keeps concrete positions; the 4 substitutes are flexible (any position). No `DEF`/`MID`/`FWD` flex slots in the XI.
11. **Shortlist/vote:** no teammate voting in MVP — shared pick control suffices; a personal shortlist bookmark remains a solo planning aid (PR-18).
12. **Odd 1v1 byes:** the draft proceeds regardless of parity; the results view flags a bye for the top-ranked participant. The app does not run the bracket.

## 20. Post-MVP state and beta operations

**The numbered MVP roadmap (PR-00 → PR-23) is complete.** Every §16 acceptance criterion has
linked automated or recorded manual evidence
([`fc-draft-web/docs/PR23_EVIDENCE.md`](fc-draft-web/docs/PR23_EVIDENCE.md)), the build is
reproducible from a clean clone, and the release collateral — runbook, retention policy,
private-beta checklist, seed/demo data — is in place. No implementation session is scheduled;
work from here is operations and an explicitly post-MVP backlog.

**Operator steps to open the private beta** (in order):

1. Run the repeated real-iPhone + desktop sessions with the prepared kit
   ([`PR23_DEVICE_SESSIONS.md`](fc-draft-web/docs/PR23_DEVICE_SESSIONS.md)) and append the
   records; any release-blocking defect reopens PR-23.
2. Work through [`PRIVATE_BETA_CHECKLIST.md`](PRIVATE_BETA_CHECKLIST.md) against the live
   deployment and invite the beta group.
3. Operate per [`RUNBOOK.md`](RUNBOOK.md): deploy verification, rollback, backup/PITR recovery,
   stuck-draft recovery, retention purges, erasure requests, and the `FcDraft.DraftRoom`
   analytics/operational instruments during the first sessions.

**Post-MVP backlog** (deliberately out of MVP scope — §3.2, §9.7, §9.9, §12.4; prioritize from
beta feedback):

- Shareable result images/public links/exports (§9.7).
- Web push notifications, pending an iPhone-PWA reliability spike (§9.9).
- Brevo-templated announcements (the one remaining labelled `Coming soon` control, after the
  §8.2 admin Overview dashboard shipped in PR-24) and bulk CSV user import (§9.2).
- An OpenTelemetry exporter for the existing meter, and automated retention purges replacing the
  operator's quarterly procedure (RETENTION_POLICY §3).
- An in-app erasure/pseudonymization admin action replacing the RUNBOOK SQL step.
- Multi-instance hosting with a SignalR backplane if the community outgrows the single-instance
  ceiling (§12.1) — revisit the retention policy at the same time (RETENTION_POLICY §4).
- Next FC dataset season: import → validate → activate through the existing versioned pipeline;
  historical drafts stay pinned by design.
