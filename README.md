# The Draft Room

Private, live tournament drafting for FC 26 men's Kick Off squads. The repository contains a .NET 8 Clean Architecture API and a responsive React PWA. The persistent platform, accounts, security, dataset, and draft-configuration slices (PR-04–PR-09) are complete; the live draft engine (PR-10+) is next.

## Current slice

- JWT sign-in through thin MediatR controllers, with mandatory first-login password change enforced **server-side** (a must-change token reaches only the change-password endpoint).
- Authentication security: BCrypt hashing, failed-login rate limiting + temporary lockout, forgot/reset-password (single-use, hashed tokens), sign-out-everywhere, and per-request security-stamp validation so password change/reset, deactivation, and admin actions **revoke older tokens immediately**.
- An append-only security-audit trail (sign-in, failed sign-in, reset, revoke, activation/deactivation, password change).
- Durable user directory: database-side search/pagination, historical retention (no hard delete), and optional avatar/preferred team name.
- Durable Brevo email **outbox**: account creation commits even during a Brevo outage; a background worker delivers with retry/backoff and clears the secret after send. `GET /api/admin/email-outbox` exposes delivery status without secrets.
- Versioned FC 26 dataset import: validate → import as a draft version → inspect issues → activate (archives the previous version, retains history). Bundled dataset seeds a fresh database.
- Server-backed Player Explorer: `/api/players` paged search, position/rating/club/league/nation filters, and name/overall sort over the active dataset (excluded/inactive content never appears).
- Versioned roster templates (locked 4-3-3 default, 120s timer) and admin curation of eligible five-star Kick Off clubs.
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

The seeded player lets you exercise the deactivation and (future) lobby flows
locally without sending a real invitation. The in-memory identity store resets
whenever the API restarts. Create additional player accounts from
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
  deactivation enforcement, and the read-only dataset/explorer/roster-template endpoints.
- `tests/FcDraft.Api.DatabaseTests` — boots the real API against a throwaway PostgreSQL container
  (via [Testcontainers](https://dotnet.testcontainers.org/)) and proves the database definitions of
  done: migration-created schema, restart persistence, DB-side user paging + retention, security-stamp
  revocation across restart, the durable email outbox (commit-during-outage → retry → delivery), the
  versioned dataset import (validate → activate → archive), the explorer query boundary (excluded
  content never appears), and roster-template/club-eligibility management. These tests **skip
  automatically when Docker is not running**, and run for real in CI.

Frontend (`fc-draft-web/`):

```bash
npm run test:run          # Vitest component tests (route guards, login flow, API errors)
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
