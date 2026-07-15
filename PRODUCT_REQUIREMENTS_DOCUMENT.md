# The Draft Room — Product Requirements Document

**Document status:** Draft v0.16 (PR-09 complete)  
**Date:** 15 July 2026  
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
6. **Club-selection round:** in spinner order, each draft team chooses an eligible real-world five-star FC 26 Kick Off club and protects one eligible footballer from that club.
7. **Position rounds:** selection starts at `ST`, followed by `LW`, then `RW`, and continues through the approved formation sequence. Every draft team receives one 120-second turn for the active position.
8. **Eligibility:** the active-position pool shows only men's base/Kick Off footballers rated 75 or higher whose primary or alternate position matches the slot.
9. **Player detail:** every visible card exposes overall rating, card stats, primary and alternate positions, role familiarity including `+`/`++`, PlayStyles, club, league, and nation.
10. **Excluded content:** women, Icons, Heroes, Ultimate Team special cards, and custom/historical cards are excluded.
11. **Real-time requirement:** lobby presence, seeds, teams, spinner result, club choices, protected players, timer, picks, and squad state update live for every connected client.

The following draft rules were open in earlier drafts and are now **locked in PR-01**. The authoritative matrix, roster template, acceptance examples, and derived database constraints live in [`DRAFT_RULES.md`](DRAFT_RULES.md); the summary is:

1. **2v2 pick authority:** either teammate may confirm the team's pick during its turn; the first valid server-accepted submission wins.
2. **Held player:** each team drafts one protected footballer from its chosen five-star club into a **separate, dedicated squad slot** (a 12th member outside the starting XI). The held footballer is removed from every team's pool but does not fill or skip a formation position.
3. **Club uniqueness:** each five-star club may be chosen by only one draft team in a lobby.
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
5. In spinner order, each team selects an available five-star club and protects one eligible footballer from it.
6. The app opens the `ST` round. Each team receives 120 seconds to make an eligible pick, then the draft advances through `LW`, `RW`, and the remaining configured positions.
7. The result page shows every squad, pick timeline, and summary statistics for the later Kick Off tournament.

### 7.3 Create and launch a 2v2 draft

1. A player creates a 2v2 lobby, becomes host, and invites 4–16 users in even-numbered increments.
2. The host assigns every present participant to Seed 1 or Seed 2; all clients see changes live.
3. The host forms up to eight teams. The system blocks any team that does not contain exactly one Seed 1 and one Seed 2 participant.
4. Participants confirm teams and ready up. Only the host can start.
5. The host starts the spinner; the server randomizes and permanently records the order of all formed teams.
6. In spinner order, each team chooses an available five-star club and protects one eligible footballer from it.
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
- After ranking, teams select in order from FC 26 men's Kick Off clubs rated five stars.
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
- `AdminRecoveryApplied`

Events are append-only. Current draft state may be stored as a projection for fast reads, but history must never be reconstructed solely from the mutable current row.

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
- **Hosting:** one process, port, origin, and deployable bundle. ASP.NET serves the built React PWA and `/api` endpoints.
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
- Define retention and deletion policy before production launch.

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

## 16. MVP acceptance criteria

The MVP is releasable when:

1. An admin can create an account with temporary password `Draft@1234`, trigger a working invite email, and the user is forced to choose a new password before entering the app.
2. Password fields have working accessible eye controls, and all visible scaffold navigation/actions are linked or explicitly disabled as `Coming soon`.
3. A host can run a 1v1 lobby with 2–10 participants and a 2v2 lobby with 4–16 participants; capacity and even-team rules are enforced.
4. A 2v2 host can assign lobby seeds and form teams containing exactly one Seed 1 and one Seed 2 player.
5. Only the host can start a ready lobby, and the server commits a random spinner order for every formed team.
6. In spinner order, teams can choose from five-star Kick Off clubs and protect one eligible player.
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

#### [ ] PR-10 — Persistent draft aggregate and append-only event history

**Outcome:** Create the authoritative draft lifecycle foundation.

**Scope:** Add Draft, DraftParticipant, DraftTeam, DraftTeamMember, DraftRosterSlot, DraftEvent, status transitions, dataset/template snapshots, optimistic versioning, and audited command handlers.

**Done when:** Allowed transitions match §10, invalid transitions fail without partial writes, every accepted transition appends an immutable event, and current state can be rebuilt or verified from history.

#### [ ] PR-11 — Lobby creation, invitations, and attendance

**Outcome:** Turn room creation into a usable 1v1/2v2 lobby.

**Scope:** Select a name, format, roster template, and expected participants; make the creator host; add join/presence states, invite/remove/replace actions, lobby detail route, and capacity/even-count validation.

**Done when:** 1v1 enforces 2–10 and 2v2 enforces 4–16 even participants server-side; deactivated users are rejected; all participants can reopen the authoritative lobby snapshot.

#### [ ] PR-12 — 2v2 seeding, team formation, and ready check

**Outcome:** Make both formats start-ready under the confirmed rules.

**Scope:** Add host-only Seed 1/Seed 2 assignment, solo-team projection for 1v1, paired-team formation for 2v2, ready/unready actions, attendance confirmation, validation summaries, and configuration freeze rules.

**Done when:** Every 2v2 team has exactly one Seed 1 and one Seed 2; only the host can change formation; Start remains disabled until attendance, assignments, and readiness pass; changes update the draft version and event history.

#### [ ] PR-13 — Server-authoritative spinner ranking

**Outcome:** Commit and reveal a fair, durable team order.

**Scope:** Add host-only start authorization, unbiased server shuffle, unique rank constraints, committed/revealed events, deterministic test seams, animated reveal UI, and reduced-motion equivalent.

**Done when:** The visual wheel cannot influence the result, all teams receive one unique stored rank, retries cannot reshuffle a committed result, and non-host commands are rejected.

### 17.7 Club selection and live draft engine

#### [ ] PR-14 — Five-star club and protected-player round

**Outcome:** Complete the pre-draft selection round in spinner order.

**Scope:** Add ordered club turns, club eligibility/uniqueness rules, club details, protected-player eligibility, slot linkage under the PR-01 decision, availability updates, and transition to position drafting.

**Done when:** Each team completes one valid club/protected-player choice, stale or duplicate choices are rejected transactionally, and the first position cannot begin until every protected player is locked.

#### [ ] PR-15 — Position draft state machine and pick validation

**Outcome:** Support authoritative position-by-position drafting.

**Scope:** Start at `ST → LW → RW`, advance through the approved template, enforce turn/team/position/rating/dataset/availability rules, accept the first valid teammate submission, add idempotency and draft-version conflicts, and append accepted-pick events.

**Done when:** Duplicate, stale, out-of-turn, ineligible, and wrong-state picks are rejected; one footballer/slot/turn can win only once; a complete final slot transitions the draft to Completed.

#### [ ] PR-16 — Server timer and host controls

**Outcome:** Make time and exceptional draft states authoritative.

**Scope:** Add the 120-second server clock, 15-second warning, approved expiry behavior, host pause/resume/cancel with reason, admin recovery authorization, timer restoration, and compensating audit events.

**Done when:** Refresh/restart-safe state computes the same remaining time, paused time does not elapse, unauthorized controls fail, and cancellation/recovery never deletes original history.

#### [ ] PR-17 — SignalR synchronization and reconnection

**Outcome:** Synchronize the entire lobby and draft across clients.

**Scope:** Add authenticated draft hubs/groups, typed events for presence, seeds, teams, readiness, spinner, clubs, protected players, timer, picks, squads, and controls; add reconnect snapshot/version reconciliation and connection-status UI primitives.

**Done when:** Multi-client integration tests show accepted state within the live propagation target, reconnect restores an authoritative snapshot without duplicated actions, and clients refresh cleanly after a version conflict.

#### [ ] PR-18 — Live draft room experience

**Outcome:** Deliver the complete desktop and one-handed iPhone drafting workflow.

**Scope:** Build active team/position/timer header, eligible player search and filters, player detail, confirmation sheet, shortlist, compact team rail, focused squad, recent picks, full history, sticky mobile action area, connection/reconnect states, and pick/turn motion.

**Done when:** A participant can complete every pick without leaving the room at 375 px; all actions work by keyboard and touch; critical state is explicit without animation; unavailable players and stale-command recovery are understandable.

### 17.8 Completion and operations

#### [ ] PR-19 — Results and squad archive

**Outcome:** Preserve and reopen completed drafts.

**Scope:** Create immutable result snapshots, formation/list squad views, average and line ratings, club/league/nation summaries, pick sequence, Draft Hub active/upcoming/completed views, Teams archive, and read-only completed-draft routes.

**Done when:** Later dataset/template changes cannot alter historical results and every participant/admin can reopen an authorized completed draft.

#### [ ] PR-20 — Player notifications and draft emails

**Outcome:** Complete essential participant communications.

**Scope:** Add persistent per-user notifications, unread badge, mark-read behavior, email preferences, and outbox-backed draft invitation, reminder, cancellation, and completion messages.

**Done when:** Notifications are authorization-scoped, survive restart, deep-link to the correct draft, and optional preferences never suppress mandatory security/service messages.

#### [ ] PR-21 — Admin communications, draft operations, and audit views

**Outcome:** Complete MVP administration and recovery.

**Scope:** Add announcement audience/preview/confirmation, Brevo campaign metadata, draft inspect/pause/resume/cancel operations, immutable audit-log queries, reason capture, delivery visibility, and role-aware Admin versus Player context.

**Done when:** Bulk sends are confirmed and throttled, every admin/host action is attributable, audit records cannot be edited/deleted through normal APIs, and recovery uses compensating events.

### 17.9 Release hardening

#### [ ] PR-22 — PWA lifecycle, accessibility, performance, and observability

**Outcome:** Meet the non-functional release requirements.

**Scope:** Add offline/reconnecting behavior, block offline mutations, install and iOS guidance, service-worker update prompt/version handshake, safe-area/orientation/keyboard handling, structured logs, correlation IDs, metrics, error monitoring hooks, and performance/accessibility fixes.

**Done when:** Core journeys pass automated accessibility checks plus manual keyboard, reduced-motion, light/dark contrast, 44×44 px touch-target, 375 px, landscape, and safe-area reviews; authenticated API data is not cached; measured performance is recorded against §14.

#### [ ] PR-23 — End-to-end MVP verification and private-beta release

**Outcome:** Prove all 13 acceptance criteria in realistic sessions.

**Scope:** Add complete 1v1 and 2v2 multi-client E2E suites, concurrency/race tests, reconnect and Brevo-outage scenarios, deployment/runbook documentation, seed/demo data, backup/recovery notes, retention policy, analytics events, and a private-beta checklist.

**Done when:** Every item in §16 has linked automated or recorded manual evidence, repeated real-iPhone and desktop sessions complete successfully, no release-blocking defect remains, and the private-beta build is reproducible from a clean environment.

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
2. **Held player:** drafted from the chosen five-star club into a separate 12th squad slot outside the XI; removed from every team's pool; it does not fill or skip a formation position.
3. **Club uniqueness:** each five-star club may be chosen by only one draft team per lobby.
4. **Footballer uniqueness:** globally unique — once held or drafted, a footballer is unavailable to all teams.
5. **Round order:** snake (reversing each position/bench round); the pre-draft club/held round is straight spinner order.
6. **2v2 pick authority:** either teammate may confirm; the first valid server-accepted submission wins.
7. **Timer expiry:** auto-pick the highest-rated available eligible footballer for the active slot (tie-break overall → name → id).
8. **Host permission:** any active (non-deactivated) user may create/host a lobby.
9. **Data source:** EA public FC 26 ratings feed is authoritative; Role/Role++ and PlayStyles supplemented from secondary sources; licensed media deferred until rights are confirmed.
10. **Substitutes:** 16-player squad — the XI keeps concrete positions; the 4 substitutes are flexible (any position). No `DEF`/`MID`/`FWD` flex slots in the XI.
11. **Shortlist/vote:** no teammate voting in MVP — shared pick control suffices; a personal shortlist bookmark remains a solo planning aid (PR-18).
12. **Odd 1v1 byes:** the draft proceeds regardless of parity; the results view flags a bye for the top-ranked participant. The app does not run the bracket.

## 20. Recommended next session

With the persistent platform and accounts complete (PR-04–PR-06), the dataset and
draft configuration in place (PR-07–PR-09), the next implementation session is
**PR-10 — Persistent draft aggregate and append-only event history** (PRD §17.6).
It introduces the authoritative draft lifecycle: `Draft`, `DraftParticipant`,
`DraftTeam`, `DraftTeamMember`, `DraftRosterSlot`, and `DraftEvent`, with the state
transitions in §10, optimistic versioning, and audited command handlers that
snapshot the active roster template (PR-09) and pinned dataset version (PR-07).
The existing canonical moodboard and persisted design system remain the approved
UI direction.
