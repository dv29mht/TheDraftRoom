# Deployment

The Draft Room deploys as a **single container behind one URL**. The .NET API serves the compiled
React SPA from its own `wwwroot`, so the browser talks to one origin — no CORS, no separate frontend
host. The database is a managed Postgres instance.

```
            ┌──────────────────────────────────────┐        ┌──────────────────┐
  Browser ─▶│  Render (one Docker web service)      │  SQL   │  Neon Postgres   │
   (HTTPS)  │  React SPA  +  .NET API  +  /api  +ws  │ ─────▶ │  (free, managed) │
            └──────────────────────────────────────┘        └──────────────────┘
                         one URL, e.g. https://the-draft-room.onrender.com
```

> **Live deployment:** production runs on **Google Cloud Run** (service `the-draft-room`, region `us-east4`,
> project `909367690008`) with a **Neon** Postgres database, and **auto-deploys on every push to `main`** —
> see [Continuous deployment (Google Cloud Run)](#continuous-deployment-google-cloud-run) below. The Render
> steps that follow are a still-valid **alternative** free-tier path (both use the same `Dockerfile`); they
> are **not** the current live target.

The Render path below uses **free tiers**: Render (web service) + Neon (Postgres).

---

## What builds into the image

`Dockerfile` is multi-stage:

1. **Node stage** — `npm ci && npm run build` in `fc-draft-web/` → `dist/`.
2. **.NET stage** — `dotnet publish -c Release` of `FcDraft.API` (the project's own frontend build
   target is skipped with `-p:SkipFrontendBuild=true` because the Node stage already built it), then
   the SPA `dist/` is copied into `wwwroot/`.
3. **Runtime stage** — `mcr.microsoft.com/dotnet/aspnet:8.0` running `dotnet FcDraft.API.dll`.

The app binds to the `PORT` the platform injects and trusts the platform's `X-Forwarded-Proto`
header (see `Program.cs`), so it runs correctly behind Render's TLS-terminating proxy.

---

## Continuous deployment (Google Cloud Run)

The production service runs on **Google Cloud Run** (`the-draft-room`, region `us-east4`, project
`909367690008`) backed by **Neon** Postgres. Pushes to `main` deploy automatically via
[`.github/workflows/deploy-cloud-run.yml`](.github/workflows/deploy-cloud-run.yml): once the CI workflow
passes, it runs `gcloud run deploy --source .` (Cloud Build builds this repo's `Dockerfile`) and rolls out
a new revision with `--max-instances 1`. Authentication is **keyless** via Workload Identity Federation —
no service-account key is stored anywhere.

### One-time setup

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

After that, every push to `main` deploys once CI is green; you can also trigger it manually from the repo's
**Actions → Deploy to Cloud Run → Run workflow**. Env vars already set on the service (the Neon connection
string, `Jwt__Key`, `Database__*`, `Brevo__*`) persist across revisions, so the workflow never touches them.

---

## Step 1 — Create the database (Neon)

1. Sign up at <https://neon.tech> and create a project (choose a region near your players).
2. Open **Connection Details** and select the **.NET** / key-value format. It looks like:

   ```
   Host=ep-xxxx-xxxx.us-east-2.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=********;SSL Mode=Require;
   ```

   Copy the whole string — this is your `ConnectionStrings__DraftRoom`. (Npgsql needs the key-value
   form, **not** the `postgresql://…` URL form. `SSL Mode=Require` is mandatory for Neon.)

---

## Step 2 — Push the repo to GitHub

Render deploys from a Git repo. If you haven't yet:

```bash
git remote add origin https://github.com/<you>/<repo>.git
git push -u origin main
```

---

## Step 3 — Create the Render service

Two options — the Blueprint is easiest:

**A. Blueprint (recommended).** In Render: **New → Blueprint**, pick this repo. Render reads
`render.yaml` and provisions the web service. It will prompt for the `sync:false` env vars.

**B. Manual.** **New → Web Service** → this repo → Runtime **Docker** → Plan **Free** → Health check
path `/health`.

Then set the environment variables:

| Variable | Value |
|----------|-------|
| `ConnectionStrings__DraftRoom` | the Neon key-value string from Step 1 |
| `Jwt__Key` | auto-generated by the Blueprint; otherwise paste a random **32+ character** secret |
| `Database__MigrateOnStartup` | `true` |
| `Database__SeedDevelopmentAccounts` | `true` for the **first** deploy only (see Step 5), else `false` |
| `Database__SeedPlayerData` | `true` (default) — imports + activates the bundled FC 26 dataset and default roster template on a fresh DB |
| `Brevo__ApiKey`, `Brevo__SenderEmail` | your Brevo credentials, or leave blank to skip invite emails |
| `Brevo__LoginUrl` | your service URL + `/login`, e.g. `https://the-draft-room.onrender.com/login` |
| `Brevo__PasswordResetUrl` | your service URL + `/reset-password` |

On first boot the app runs EF Core migrations automatically (`Database__MigrateOnStartup=true`),
creating all tables in Neon (users, security audit, password-reset tokens, email outbox, the
versioned footballer/club dataset, and roster templates), seeds the default 4-3-3 roster template, and
— when `Database__SeedPlayerData=true` — imports and activates the bundled FC 26 dataset.

---

## Step 4 — Verify

- `https://<your-app>.onrender.com/health` → `{"status":"healthy", ...}` (returns 503 if Postgres is
  unreachable — that's the DB health check working).
- `https://<your-app>.onrender.com/` → the app loads.
- `https://<your-app>.onrender.com/swagger` → the API explorer.

---

## Step 5 — Bootstrap the first admin

Sign-up is invite-only (admins create accounts), so the very first login needs a seeded admin:

1. Deploy once with `Database__SeedDevelopmentAccounts=true`.
2. Log in as the seeded admin — **`mdevansh@gmail.com` / `DraftAdmin@2026`** (the sole designated administrator account).
3. **Immediately change that password** — these credentials are public in this repository.
4. Create your real accounts from the admin UI.
5. Set `Database__SeedDevelopmentAccounts=false` and redeploy. (Seeding only runs against an empty
   user table, but turning it off makes that explicit.)

---

## Notes & limits (free tier)

- **Cold starts.** Render's free web service sleeps after ~15 min idle; the first request then takes
  ~30–60 s to wake. Fine for a scheduled draft — hit the URL a minute before you start.
- **Neon autosuspend.** The free database also sleeps when idle and wakes on the first query (a few
  seconds). The `/health` check may briefly report 503 while it wakes; Render retries.
- **One instance / real-time.** Live draft state is in memory and WebSocket clients must share one
  process, so the service stays at a single instance (`numInstances: 1`). Don't enable autoscaling.
- **Secrets.** Never ship the dev `Jwt:Key` from `appsettings.json` (it's a placeholder). The
  Blueprint generates a real one; if deploying manually, set a strong 32+ char `Jwt__Key`.

## Upgrade path (smooth real-time, ~a few $/mo)

When you want no cold starts, move the same container to **Fly.io** (`fly launch` reads this
Dockerfile; set the same env vars via `fly secrets set`). Fly keeps the instance warm and has
first-class WebSocket support — nothing in the app changes.
