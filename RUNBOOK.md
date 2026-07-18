# Operations runbook — The Draft Room

**Live deployment:** Google Cloud Run service **`the-draft-room`**, region **`us-east4`**,
project **`909367690008`**, backed by **Google Cloud SQL** (PostgreSQL, same region, reached over
the Cloud SQL Auth Proxy socket). Single instance, single container,
one origin: `https://the-draft-room-909367690008.us-east4.run.app`.

This is the operational reference for the REAL production path. [`DEPLOYMENT.md`](DEPLOYMENT.md)
documents the one-time provisioning steps (WIF setup, Cloud SQL creation, first-admin bootstrap);
`render.yaml` is a **legacy/alternative** Render blueprint and is *not* the live target.

---

## 1. How a deploy happens

1. Push (or merge a PR) to `main`.
2. [`ci.yml`](.github/workflows/ci.yml) runs: backend tests (incl. Testcontainers PostgreSQL),
   frontend Vitest + build, Playwright client-only e2e, full-stack e2e, and a Dockerfile
   container build.
3. On CI success, [`deploy-cloud-run.yml`](.github/workflows/deploy-cloud-run.yml) fires
   (`workflow_run` trigger): checks out the exact commit CI validated, authenticates with
   **keyless Workload Identity Federation** (repo variables `GCP_PROJECT_ID`,
   `GCP_WIF_PROVIDER`, `GCP_DEPLOY_SA` — no stored keys), and runs
   `gcloud run deploy the-draft-room --source . --region us-east4 --max-instances 1`.
   Cloud Build builds the repo `Dockerfile`; Cloud Run rolls out a new revision.
4. On boot the app applies pending EF Core migrations (`Database__MigrateOnStartup=true`).
   Migrations are **forward-safe only** — additive, never destructive — so a rollback to the
   previous revision keeps working against the migrated schema.

Manual deploy: GitHub → Actions → **Deploy to Cloud Run** → *Run workflow* (uses `main`).

### Verify a deploy

- `GET /health` → `200` with `status: healthy`, the compiled-in `contract`, and the new
  `revision` (Cloud Run `K_REVISION`) — confirm the revision changed.
- `GET /api/meta/version` → `{contract, revision}` (anonymous).
- Open the app: connected clients on the old shell receive the update prompt via the
  contract handshake / service-worker update check (hourly + on foreground).

### What a deploy does to a LIVE draft

Draft state (status, picks, version, the 120 s turn anchor `turn_started_at`) is **in the
database**, so a mid-draft deploy does not lose the draft or the clock. What restarts is the
process: SignalR is **in-process** (no backplane), so every websocket drops, clients
auto-reconnect to the new revision, rejoin their draft group, and reconcile from the
authoritative snapshot (§7.4). Expired turns are caught up lazily plus by the 5 s sweep.
Prefer deploying outside scheduled draft sessions anyway; players see a brief
"Reconnecting…" banner.

## 2. Rollback

1. Cloud Run console → service `the-draft-room` → **Revisions** → pick the previous healthy
   revision → **Manage traffic** → route 100 % to it. (No `gcloud` locally? The console path
   is the supported one; any machine with `gcloud` can run
   `gcloud run services update-traffic the-draft-room --region us-east4 --to-revisions REV=100`.)
2. Schema: migrations are forward-safe/additive, so the previous app runs against the newer
   schema. Never roll back the database to undo a deploy — see §4 for data recovery.
3. A rolled-back shell may present an older client contract; the handshake only forces
   refreshes forward, so no action needed.

## 3. Configuration inventory (Cloud Run service env vars)

Set once on the service; **the deploy workflow never touches them**, so they persist across
revisions:

| Variable | Purpose |
|---|---|
| `ConnectionStrings__DraftRoom` | Cloud SQL socket connection string (`Host=/cloudsql/PROJECT:us-east4:draftroom-db;Database=…;Username=…;Password=…`) — presence switches the app onto PostgreSQL. The instance is attached to the service via `--add-cloudsql-instances` (a *Connection*, not an env var); the runtime SA holds `roles/cloudsql.client`. |
| `Jwt__Key` | 32+ char signing secret (never the committed placeholder) |
| `Database__MigrateOnStartup` | `true` — schema comes exclusively from migrations |
| `Database__SeedDevelopmentAccounts` | `false` after first bootstrap (see DEPLOYMENT.md Step 5) |
| `Database__SeedDemoAccounts` | `false` in production — Testing/demo-only seeded player accounts |
| `Database__SeedPlayerData` | `true` — bundled FC 26 dataset + default template on a fresh DB |
| `Brevo__ApiKey`, `Brevo__SenderEmail`, `Brevo__SenderName` | Live email credentials (server-only) |
| `Brevo__LoginUrl`, `Brevo__PasswordResetUrl`, `Brevo__AppBaseUrl` | Links embedded in emails |

Single instance is **mandatory** (`--max-instances 1`): live SignalR groups and the login
throttle are in-process. Do not enable autoscaling; scale-up would split websocket clients
across processes with no backplane.

## 4. Backup and recovery (Cloud SQL)

- **Backups are Cloud SQL automated daily backups + point-in-time recovery** (WAL-based), not
  manual dumps. Enabled at instance creation (`--backup-start-time`, `--retained-backups-count 7`,
  `--enable-point-in-time-recovery`). Verify/adjust in Cloud console → SQL → instance `draftroom-db`
  → **Backups** before the beta if longer cover is wanted.
- **Recovery (clone to a timestamp, non-destructive — preferred):** `gcloud sql instances clone
  draftroom-db draftroom-db-restore --point-in-time '<RFC3339 timestamp just before the incident>'`
  → verify the clone's data (connect via the Auth Proxy) → point `ConnectionStrings__DraftRoom` +
  the `--add-cloudsql-instances` attachment on Cloud Run at the clone → deploy a revision restart.
  The original instance remains as evidence. (An in-place `gcloud sql backups restore` is also
  available but overwrites the instance — prefer the clone.)
- **What is at risk in-process (not in the DB):** active websocket connections, the login
  throttle's failure counters, and the email-outbox worker's in-flight attempt — all safe to
  lose; the outbox row itself is durable and retried. The in-memory storage branch (blank
  connection string) is **never** the production configuration.
- **Erasure re-application:** if a restore resurrects data pseudonymized under
  [`RETENTION_POLICY.md`](RETENTION_POLICY.md), re-run the erasure step (§6) after recovery.

## 5. Health, logs, metrics, alerts

- `GET /health` — liveness + `database` check on the SQL branch (503 while the DB is unreachable),
  plus `contract`/`revision`/`self`.
- **Logs:** Cloud Run → Logs (structured JSON console lines with scopes in Production). Every
  request carries a correlation id — echoed as `X-Correlation-Id` on responses and stamped
  into error ProblemDetails; search logs by that id when a user reports an error.
- **Metrics:** vendor-neutral `System.Diagnostics.Metrics` on the **`FcDraft.DraftRoom`**
  meter — operational instruments (`draftroom.request.duration`, `draftroom.request.failures`,
  `draftroom.errors.unhandled`) plus the §15 product analytics instruments
  (`draftroom.users.invited/activated`, `draftroom.drafts.created/started/ended`,
  `draftroom.drafts.time_to_first_pick`, `draftroom.picks.accepted`,
  `draftroom.picks.turn_duration`, `draftroom.drafts.admin_interventions`,
  `draftroom.hub.joins`, `draftroom.email.delivery`). No exporter is wired in production yet;
  attach any OTel exporter (or `dotnet-counters` locally) without code changes. Analytics
  carry only low-cardinality tags (format/outcome/action) — never ids, emails, or content.
- **Error monitoring:** `IErrorReporter` seam (default: logs + counts). Swapping in a vendor
  is one DI registration.

## 6. Standard procedures

- **Bootstrap first admin / reset seeded credentials:** DEPLOYMENT.md Step 5.
- **Create beta accounts:** Admin → Users → Add player (name + email mandatory); the invite
  email carries a unique one-time password; the user must change it at first sign-in.
- **Stuck draft:** Admin → Drafts → inspect → pause/resume/cancel with a reason (all audited,
  version-checked). Admin recovery appends compensating events; history is never edited.
- **Announcement:** Admin → Communications → compose → preview → confirm (audience re-check;
  outbox-throttled 20/15 s).
- **Erasure request:** follow [`RETENTION_POLICY.md`](RETENTION_POLICY.md) §2 — deactivate in
  the UI, then run against Cloud SQL (single transaction; replace `:id` and `N`):

  ```sql
  BEGIN;
  UPDATE users SET display_name = 'Removed player N',
                   email = 'removed-N@invalid.draftroom',
                   normalized_email = 'REMOVED-N@INVALID.DRAFTROOM',
                   avatar_url = NULL, preferred_team_name = NULL
   WHERE id = :id;
  DELETE FROM user_notifications WHERE user_id = :id;
  DELETE FROM email_outbox WHERE recipient_email ILIKE :email AND sent_at IS NULL;
  COMMIT;
  ```

- **Retention purges (quarterly, per RETENTION_POLICY §3):**

  ```sql
  DELETE FROM email_outbox
   WHERE (sent_at IS NOT NULL OR failed_at IS NOT NULL)
     AND created_at < now() - interval '12 months';
  -- security audit: only after the 24-month minimum
  DELETE FROM security_audit_events WHERE created_at < now() - interval '24 months';
  ```

  (Column names per the EF snake-case mappings; verify against the current migration set
  before running — the statements are deliberately time-scoped and idempotent.)

## 7. Known caveats

- **Cold start:** first request after idle may wait for the Cloud Run instance to wake; `/health`
  can briefly 503. (Cloud SQL stays up — it does not autosuspend.) Hit the URL a minute before a
  scheduled draft.
- **Single region, single instance:** availability target is §14's 99.5 % — there is no
  failover; an instance restart is the recovery mechanism.
- **Committed placeholder secrets:** the repo's `appsettings.json` JWT key and the seeded
  account passwords are public; production must override both (already done for the live
  service; re-check after any environment rebuild).
- **`appsettings.Development.json` holds a real Brevo key locally** and the in-memory branch
  delivers email inline — never run manual flows or E2E against environment `Development`;
  use `Testing` (see README → Full-stack E2E).
