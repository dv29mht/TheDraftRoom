# The Draft Room

Private, live tournament drafting for FC 26 men's Kick Off squads. The repository contains a .NET 8 Clean Architecture API and a responsive React PWA. The persistent platform, accounts, security, dataset, draft-configuration, lobby, team-formation, spinner, five-star club/protected-player round, and position-draft pick engine slices (PR-04–PR-15) are complete; the server timer and host controls (PR-16) are next.

## Current slice

- JWT sign-in through thin MediatR controllers, with mandatory first-login password change enforced **server-side** (a must-change token reaches only the change-password endpoint).
- Authentication security: BCrypt hashing, failed-login rate limiting + temporary lockout, forgot/reset-password (single-use, hashed tokens), sign-out-everywhere, and per-request security-stamp validation so password change/reset, deactivation, and admin actions **revoke older tokens immediately**.
- An append-only security-audit trail (sign-in, failed sign-in, reset, revoke, activation/deactivation, password change).
- Durable user directory: database-side search/pagination, historical retention (no hard delete), and optional avatar/preferred team name.
- Durable Brevo email **outbox**: account creation commits even during a Brevo outage; a background worker delivers with retry/backoff and clears the secret after send. `GET /api/admin/email-outbox` exposes delivery status without secrets.
- Versioned FC 26 dataset import: validate → import as a draft version → inspect issues → activate (archives the previous version, retains history). Bundled dataset seeds a fresh database.
- Server-backed Player Explorer: `/api/players` paged search, position/rating/club/league/nation filters, and name/overall sort over the active dataset (excluded/inactive content never appears).
- Versioned roster templates (locked 4-3-3 default, 120s timer) and admin curation of eligible five-star Kick Off clubs.
- A persistent, audited draft aggregate: create a 1v1/2v2 lobby, invite/join/remove, and lock into team formation, with server-side capacity enforcement and optimistic version conflicts; every accepted transition appends one immutable event.
- Team formation & ready check: host-only Seed 1/Seed 2 assignment (2v2), solo-team projection (1v1) and paired teams (2v2, exactly one Seed 1 + one Seed 2), self-service readiness, and a **Start** control gated on attendance + assignment + readiness.
- A server-authoritative **spinner**: an unbiased Fisher–Yates order committed on the server (injected shuffle seam), idempotent so a retry cannot reshuffle, revealed by an animated wheel with a reduced-motion-safe ordered list.
- A pre-draft **five-star club + protected-player round**: in **straight** spinner order, each team picks a unique five-star club and protects one 75+ Kick Off player from it (globally removed from every pool); the position draft cannot open until every team is set. Eligibility is scoped to the dataset the draft **pinned at start**.
- A **position-draft pick engine**: **snake** order over committed spinner ranks fills ST → LW → RW → CM×3 → LB → CB×2 → RB → GK then 4 flexible subs; either teammate (or an admin) may submit and the first valid pick wins the slot; turn, position, 75+ rating, pinned-dataset, and availability are all server-enforced, and the final slot completes the draft. Unique `(draft, footballer)` / `(team, slot)` / `(draft, club)` indexes make duplicates lose transactionally.
- Responsive shell, player/admin route guards, Swagger with Bearer auth at `/swagger`, installable PWA, and .NET + frontend test suites with a CI workflow.

The API runs an **in-memory foundation by default** so a fresh clone works without any database. Supplying a PostgreSQL connection string (see [Database persistence](#database-persistence)) switches identity, the email outbox, the dataset, and roster templates onto EF Core so everything survives a restart. Without a database, email is delivered inline and the bundled dataset / default template are served read-only.

## Run locally

Start the API:

```bash
dotnet run --project src/FcDraft.API/FcDraft.API.csproj --launch-profile http
```

Start the PWA in another terminal:

```bash
cd fc-draft-web
npm install
npm run dev
```

Open `http://localhost:5173`. Vite proxies `/api` and `/health` to `http://localhost:5088`.

Development accounts (seeded in-memory, no email is sent to them):

| Role | Email | Password |
|---|---|---|
| Admin | `mdevansh@gmail.com` | `DraftAdmin@2026` |
| Player | `player@draftroom.dev` | `Player@2026` |

The seeded player lets you exercise the deactivation and draft-lobby flows
locally without sending a real invitation. Sign in, open **Drafts → New lobby** to
create a 1v1/2v2 lobby, invite players, confirm attendance, and lock the lobby once
the capacity rules pass (1v1 2–10, 2v2 4–16 even). After locking you assign seeds and
form teams (2v2), ready up, and — once everyone is present, assigned, and ready — the
host starts the draft and spins the server-authoritative team order. The in-memory
identity store resets whenever the API restarts. Create additional player accounts from
**Admin → Users**; each is invited with a unique one-time password. From the same
directory you can deactivate a player (they can no longer sign in) and reactivate
them later; administrator accounts are protected from deactivation.

## Brevo invitations

The Brevo API key is a secret and must never be committed. The checked-in
`src/FcDraft.API/appsettings.json` leaves `Brevo:ApiKey` and `Brevo:SenderEmail`
blank, which reports `emailConfigured: false` until you supply real values.

For local development, copy the template and fill in your key:

```bash
cp src/FcDraft.API/appsettings.Development.json.example \
   src/FcDraft.API/appsettings.Development.json
```

`appsettings.Development.json` is gitignored, so the secret stays off source
control. Alternatively, set server environment variables (these take precedence
and are the recommended path for deployed environments):

```bash
Brevo__ApiKey=xkeysib-your-api-key
Brevo__SenderEmail=verified-sender@example.com
Brevo__SenderName="The Draft Room"
Brevo__LoginUrl=https://your-domain.example/login
```

The sender address must be verified in Brevo. New invitations contain a unique one-time password, and resending an invitation invalidates the previous password.

## Database persistence

The identity store runs in-memory by default. To enable durable PostgreSQL persistence, supply a
connection string under `ConnectionStrings:DraftRoom` — either in the gitignored
`appsettings.Development.json` or via the `ConnectionStrings__DraftRoom` environment variable. The
committed `appsettings.json` leaves it blank, so a fresh clone keeps the in-memory foundation. Never
commit a connection string; `appsettings.Development.json.example` documents the shape.

Start a local database (any PostgreSQL 14+ works — this example uses Docker):

```bash
docker run --name draftroom-db -e POSTGRES_PASSWORD=devpass -e POSTGRES_DB=draftroom \
  -p 5432:5432 -d postgres:16-alpine
```

Then copy the template and point it at that database:

```bash
cp src/FcDraft.API/appsettings.Development.json.example \
   src/FcDraft.API/appsettings.Development.json
# edit ConnectionStrings:DraftRoom, e.g.
# Host=localhost;Port=5432;Database=draftroom;Username=postgres;Password=devpass;
```

On startup the API applies any pending EF Core migrations (so a clean database is created
**exclusively from migrations** — no manual DDL), seeds the platform metadata and the locked
default roster template, and, when `Database:SeedDevelopmentAccounts` is `true`, seeds the
deterministic development accounts. When `Database:SeedPlayerData` is `true` (default) it also imports
and activates the bundled FC 26 dataset on a fresh database so the player explorer and draft
configuration work out of the box. The `/health` endpoint then reports a `database` check alongside
the service status.

Migration tooling (requires the `dotnet-ef` tool: `dotnet tool install --global dotnet-ef`):

```bash
# Add a migration (offline; needs no running database)
dotnet ef migrations add <Name> \
  --project src/FcDraft.Infrastructure --startup-project src/FcDraft.Infrastructure \
  --output-dir Persistence/Migrations

# Apply migrations manually (also happens automatically on API startup). The design-time factory
# reads DRAFTROOM_DESIGN_CONNECTION for this offline-tooling path:
DRAFTROOM_DESIGN_CONNECTION="Host=localhost;Port=5432;Database=draftroom;Username=postgres;Password=devpass;" \
dotnet ef database update \
  --project src/FcDraft.Infrastructure --startup-project src/FcDraft.Infrastructure
```

## Refresh the player snapshot

The checked-in dataset contains all FC 26 men's players rated 75 or higher from EA's public ratings directory. Refresh it with:

```bash
cd fc-draft-web
npm run import:players
```

EA's public ratings feed does not expose positional Role/Role++ assignments. The application data contract and UI support them, but the checked-in official snapshot leaves roles empty rather than inferring them.

## Verify

```bash
dotnet build src/FcDraft.API/FcDraft.API.csproj
cd fc-draft-web && npm run build
```

Swagger is available at `http://localhost:5088/swagger`. Use `POST /api/auth/login`, copy the returned access token, and select **Authorize** to test protected endpoints.

## Automated tests and CI

The suites are deterministic and never call Brevo or any external FC service. The
Brevo sender is replaced with an in-memory fake, which also captures the one-time
password so tests can drive the full invite → first-login → password-change flow.

Backend (.NET), from the repository root:

```bash
dotnet test FcDraft.sln            # unit + API integration + PostgreSQL persistence tests
```

- `tests/FcDraft.UnitTests` — validators, the login/change-password handlers, the in-memory identity
  service (create/invite, paging, profile fields, deactivation), the BCrypt hasher (incl. legacy-hash
  verification), the failed-login throttle, and the password-reset/session-revocation flow.
- `tests/FcDraft.Api.IntegrationTests` — boots the real API in-process with `WebApplicationFactory`
  (in-memory store, no database required) and covers login, forced-password-change **enforcement**,
  sign-out-everywhere revocation, login lockout, the forgot/reset flow, authorization boundaries,
  deactivation enforcement, the read-only dataset/explorer/roster-template endpoints, the draft
  lobby (create, reopen snapshot, invite/join/remove, deactivated-user rejection, and capacity-gated
  locking; participant-only snapshot access), team formation through the spinner (2v2 seed →
  pair → ready → start → commit end to end, the readiness-gated Start, and host-only control), and
  the **full club/position flow** (open club selection → straight-order club + protected player →
  open position draft → snake-order picks → **Completed**, plus out-of-turn/duplicate-club rejection
  and 2v2 either-teammate pick authority).
- `tests/FcDraft.Api.DatabaseTests` — boots the real API against a throwaway PostgreSQL container
  (via [Testcontainers](https://dotnet.testcontainers.org/)) and proves the database definitions of
  done: migration-created schema, restart persistence, DB-side user paging + retention, security-stamp
  revocation across restart, the durable email outbox (commit-during-outage → retry → delivery), the
  versioned dataset import (validate → activate → archive), the explorer query boundary (excluded
  content never appears), roster-template/club-eligibility management, the draft lobby (attendance
  persistence + reopen, server-side capacity enforcement, and deactivated-user rejection), team
  formation (2v2 seed/team persistence with one participant per team; spinner rank uniqueness +
  idempotency), and the club/position pick engine (straight club order → snake position order →
  Completed persists; the unique `(draft, footballer)` / `(team, slot)` / `(draft, club)` indexes
  reject duplicates transactionally). These tests **skip automatically when Docker is not running**, and run for real in CI.

Frontend (`fc-draft-web/`):

```bash
npm run test:run          # Vitest component tests (route guards, login flow, API errors, lobby/team-formation/spinner/club-selection/position-draft)
npm run test:e2e:install  # one-time: download the Chromium browser for Playwright
npm run test:e2e          # Playwright PWA smoke tests (builds, serves, drives the shell)
```

`npm run test:e2e` builds and serves the PWA itself (`vite preview`), then checks the
login screen renders, an anonymous visit to a protected route redirects to `/login`, and
the installable manifest is served. These smoke tests are client-side only, so they need
no running API.

GitHub Actions ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) runs three jobs on
every push and pull request: **backend** (restore, Release build, test — including the PostgreSQL
persistence tests, which use Testcontainers against the runner's Docker daemon), **frontend**
(`npm ci`, Vitest, production build), and **e2e** (Playwright smoke).

## Production bundle

Publishing the API in Release mode runs `npm ci` and `npm run build`, then copies the PWA into the API publish output's `wwwroot` directory:

```bash
dotnet publish src/FcDraft.API/FcDraft.API.csproj -c Release -o publish
```

ASP.NET serves the SPA and API from one origin, with SPA fallback excluded for `/api`, `/swagger`, and `/health` paths.
