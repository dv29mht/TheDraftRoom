# Deployment

The Draft Room deploys as a **single container behind one URL**. The .NET API serves the compiled
React SPA from its own `wwwroot`, so the browser talks to one origin — no CORS, no separate frontend
host. The database is a managed Postgres instance.

**The live production path is Google Cloud Run + Cloud SQL with continuous deployment from `main`** —
documented first below. Day-2 operations (deploy verification, rollback, backup/recovery, retention
procedures) live in [`RUNBOOK.md`](RUNBOOK.md). A legacy/alternative **Render** path is preserved in
the appendix at the bottom; it is **not** the live target.

```
            ┌──────────────────────────────────────┐         ┌──────────────────┐
  Browser ─▶│  Cloud Run (one container, 1 instance)│ socket  │  Cloud SQL       │
   (HTTPS)  │  React SPA  +  .NET API  +  /api  +ws  │ ──────▶ │  PostgreSQL      │
            └──────────────────────────────────────┘/cloudsql └──────────────────┘
        one URL: https://the-draft-room-909367690008.us-east4.run.app
```

The database connection is an IAM-authenticated Unix socket via Cloud Run's built-in **Cloud SQL
Auth Proxy** (`--add-cloudsql-instances`) — the instance is not exposed to the public internet.

---

## What builds into the image

`Dockerfile` is multi-stage:

1. **Node stage** — `npm ci && npm run build` in `fc-draft-web/` → `dist/`.
2. **.NET stage** — `dotnet publish -c Release` of `FcDraft.API` (the project's own frontend build
   target is skipped with `-p:SkipFrontendBuild=true` because the Node stage already built it), then
   the SPA `dist/` is copied into `wwwroot/`.
3. **Runtime stage** — `mcr.microsoft.com/dotnet/aspnet:8.0` running `dotnet FcDraft.API.dll`.

The app binds to the `PORT` the platform injects and trusts the platform's `X-Forwarded-Proto`
header (see `Program.cs`), so it runs correctly behind a TLS-terminating proxy. CI builds this same
image from a clean checkout on every run (the `container` job), so the deploy path is continuously
proven reproducible.

---

## Production: Google Cloud Run + Cloud SQL (the live path)

The production service is **`the-draft-room`**, region **`us-east4`**, project **`909367690008`**,
backed by **Cloud SQL** (PostgreSQL) in the same region. Pushes to `main` deploy automatically via
[`.github/workflows/deploy-cloud-run.yml`](.github/workflows/deploy-cloud-run.yml): once the CI
workflow passes, it runs `gcloud run deploy --source .` (Cloud Build builds this repo's
`Dockerfile`) and rolls out a new revision with `--max-instances 1`. Authentication is **keyless**
via Workload Identity Federation — no service-account key is stored anywhere.

### Step 1 — Create the database (Google Cloud SQL)

Run once in a terminal with `gcloud` authenticated to the project (or in Cloud Shell). This creates
the managed Postgres instance, a database, an application user, and turns on automated backups +
point-in-time recovery (the backup mechanism — see RUNBOOK §4):

```bash
PROJECT_ID=$(gcloud config get-value project)
INSTANCE=draftroom-db
REGION=us-east4
ICN="$PROJECT_ID:$REGION:$INSTANCE"           # instance connection name

gcloud sql instances create "$INSTANCE" --project="$PROJECT_ID" --region="$REGION" \
  --database-version=POSTGRES_17 \            # match your data's major version
  --tier=db-f1-micro \                        # smallest/cheapest; bump to db-g1-small if memory-tight
  --storage-size=10GB --storage-auto-increase \
  --backup-start-time=07:00 --retained-backups-count=7 \
  --enable-point-in-time-recovery

gcloud sql users set-password postgres --instance="$INSTANCE" --password='POSTGRES_PW'
gcloud sql databases create draftroom --instance="$INSTANCE"
gcloud sql users create draftroom_app --instance="$INSTANCE" --password='APP_PW'
```

The app reaches Cloud SQL through Cloud Run's built-in **Cloud SQL Auth Proxy** (IAM-authenticated
Unix socket) — no public IP exposure, no firewall allowlist. Grant the runtime service account the
client role:

```bash
RUNTIME_SA=$(gcloud run services describe the-draft-room --region="$REGION" \
  --format='value(spec.template.spec.serviceAccountName)')
RUNTIME_SA=${RUNTIME_SA:-$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')-compute@developer.gserviceaccount.com}
gcloud projects add-iam-policy-binding "$PROJECT_ID" \
  --member="serviceAccount:$RUNTIME_SA" --role="roles/cloudsql.client"
```

Your `ConnectionStrings__DraftRoom` is the **socket form** below (Npgsql accepts it verbatim — no
`SSL Mode`, because the proxy terminates TLS). You attach the instance to the service and set this
env var together in Step 3, so both land on the same revision:

```
Host=/cloudsql/PROJECT_ID:us-east4:draftroom-db;Database=draftroom;Username=draftroom_app;Password=APP_PW
```

### Step 2 — One-time Workload Identity Federation setup

Run once, in a terminal with `gcloud` authenticated to the project (`gcloud auth login`, then
`gcloud config set project …`):

```bash
# PROJECT_ID must be the alphanumeric project id (it forms the service-account email);
# PROJECT_NUMBER (909367690008, from the Cloud Run URL) is what the WIF principal uses.
PROJECT_ID=$(gcloud config get-value project)
PROJECT_NUMBER=$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')
REPO=dv29mht/TheDraftRoom        # owner/repo
SA=github-deployer
POOL=github-pool
PROVIDER=github-provider

# 1. Enable the APIs a source deploy uses.
gcloud services enable run.googleapis.com cloudbuild.googleapis.com \
  artifactregistry.googleapis.com iamcredentials.googleapis.com --project "$PROJECT_ID"

# 2. Create a dedicated deploy service account + grant it deploy permissions.
gcloud iam service-accounts create "$SA" --project "$PROJECT_ID" --display-name "GitHub Actions deployer"
SA_EMAIL="$SA@$PROJECT_ID.iam.gserviceaccount.com"
for ROLE in roles/run.admin roles/cloudbuild.builds.editor roles/artifactregistry.writer \
            roles/iam.serviceAccountUser roles/storage.admin; do
  gcloud projects add-iam-policy-binding "$PROJECT_ID" --member="serviceAccount:$SA_EMAIL" --role="$ROLE"
done

# 3. Create a Workload Identity pool + GitHub OIDC provider, scoped to this repo only.
gcloud iam workload-identity-pools create "$POOL" --project "$PROJECT_ID" --location=global \
  --display-name="GitHub pool"
gcloud iam workload-identity-pools providers create-oidc "$PROVIDER" --project "$PROJECT_ID" \
  --location=global --workload-identity-pool="$POOL" --display-name="GitHub provider" \
  --issuer-uri="https://token.actions.githubusercontent.com" \
  --attribute-mapping="google.subject=assertion.sub,attribute.repository=assertion.repository" \
  --attribute-condition="assertion.repository=='$REPO'"

# 4. Let this repo's Actions impersonate the deploy service account.
gcloud iam service-accounts add-iam-policy-binding "$SA_EMAIL" --project "$PROJECT_ID" \
  --role=roles/iam.workloadIdentityUser \
  --member="principalSet://iam.googleapis.com/projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/$POOL/attribute.repository/$REPO"

# 5. Print the three values for the GitHub variables.
echo "GCP_PROJECT_ID   = $PROJECT_ID"
echo "GCP_DEPLOY_SA    = $SA_EMAIL"
echo "GCP_WIF_PROVIDER = projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/$POOL/providers/$PROVIDER"
```

Then add three **repository variables** (GitHub → **Settings → Secrets and variables → Actions → Variables**):

| Variable | Value |
|----------|-------|
| `GCP_PROJECT_ID` | your project id (the `$PROJECT_ID` above; the number `909367690008` also works) |
| `GCP_DEPLOY_SA` | `github-deployer@<project-id>.iam.gserviceaccount.com` |
| `GCP_WIF_PROVIDER` | the `projects/…/providers/github-provider` string from step 5 |

### Step 3 — Configure the service environment

Set once on the Cloud Run service (Console → service → *Edit & deploy new revision* → Variables, or
`gcloud run services update`); the deploy workflow never touches them, so they persist across
revisions. In the **same** revision, attach the Cloud SQL instance (Console → *Connections* tab → add
`draftroom-db`, or `gcloud run services update the-draft-room --region us-east4 --add-cloudsql-instances "$ICN"`)
— the attachment likewise persists across `--source` deploys, so the workflow needs no change.

| Variable | Value |
|----------|-------|
| `ConnectionStrings__DraftRoom` | the Cloud SQL socket string from Step 1 (`Host=/cloudsql/PROJECT_ID:us-east4:draftroom-db;…`) |
| `Jwt__Key` | a random **32+ character** secret (never the committed placeholder) |
| `Database__MigrateOnStartup` | `true` |
| `Database__SeedDevelopmentAccounts` | `true` for the **first** deploy only (see Step 5), else `false` |
| `Database__SeedDemoAccounts` | `false` — Testing/demo-only accounts, never production |
| `Database__SeedPlayerData` | `true` (default) — imports + activates the bundled FC 26 dataset and default roster template on a fresh DB |
| `Brevo__ApiKey`, `Brevo__SenderEmail` | your Brevo credentials (sender must be verified in Brevo) |
| `Brevo__LoginUrl` | the service URL + `/login` |
| `Brevo__PasswordResetUrl` | the service URL + `/reset-password` |

The service must stay at **`--max-instances 1`** (the deploy workflow re-asserts it): live SignalR
groups are in-process and must share one process. Do not enable autoscaling.

### Step 4 — Deploy and verify

Merge/push to `main` (or Actions → **Deploy to Cloud Run** → *Run workflow*). Then:

- `/health` → `{"status":"healthy", …}` with `contract` and the new `revision` (a brief 503 while
  the DB connection warms up is the health check working).
- `/` → the app loads; `/swagger` → the API explorer.
- On first boot the app applies EF Core migrations automatically, creating every table in Cloud SQL,
  seeds the default 4-3-3 roster template, and — when `Database__SeedPlayerData=true` — imports and
  activates the bundled FC 26 dataset.

### Step 5 — Bootstrap the first admin

Sign-up is invite-only (admins create accounts), so the very first login needs a seeded admin:

1. Deploy once with `Database__SeedDevelopmentAccounts=true`.
2. Log in as the seeded admin — **`mdevansh@gmail.com` / `DraftAdmin@2026`** (the sole designated administrator account).
3. **Immediately change that password** — these credentials are public in this repository.
4. Create your real accounts from the admin UI.
5. Set `Database__SeedDevelopmentAccounts=false` and redeploy. (Seeding only runs against an empty
   user table, but turning it off makes that explicit.)

### Operating notes

- **Cold starts:** the Cloud Run instance sleeps when idle (Cloud SQL stays up — it does not
  autosuspend), so the first request after idle waits for the container to wake; hit the URL a minute
  before a scheduled draft.
- **One instance / real-time:** live draft state synchronization requires all WebSocket clients on
  one process. Never scale horizontally without adding a SignalR backplane first.
- **Secrets:** never ship the dev `Jwt:Key` from `appsettings.json` (it's a placeholder); set a
  strong 32+ char `Jwt__Key`.
- **Rollback, backups, retention, erasure:** see [`RUNBOOK.md`](RUNBOOK.md).

---

## Appendix — legacy/alternative path: Render (not the live target)

> ⚠️ These steps provisioned the original free-tier target and are kept only as a working
> alternative (both paths use the same `Dockerfile`). The committed [`render.yaml`](render.yaml)
> blueprint belongs to this path. **Production runs on Cloud Run** — see above.

1. **Push the repo to GitHub** (Render deploys from a Git repo).
2. **Create the service** — either **New → Blueprint** and pick this repo (Render reads
   `render.yaml` and prompts for the `sync:false` env vars), or manually: **New → Web Service** →
   this repo → Runtime **Docker** → Plan **Free** → Health check path `/health`.
3. **Set the environment variables** — the same table as Step 3 above (the Blueprint auto-generates
   `Jwt__Key`).
4. **Verify and bootstrap** — the same Step 4/Step 5 flow, on `https://<your-app>.onrender.com`.

Free-tier notes: the web service sleeps after ~15 min idle (first request takes ~30–60 s to wake);
keep `numInstances: 1` for the same single-process reason as above. If you want no cold starts for
a few $/mo, the same container also runs unchanged on Fly.io (`fly launch` reads this Dockerfile).
