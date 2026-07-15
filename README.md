# The Draft Room

Private, live tournament drafting for FC 26 men's Kick Off squads. The repository currently contains the first runnable foundation: a .NET 8 Clean Architecture API and a responsive React PWA.

## Current slice

- JWT sign-in through thin MediatR controllers.
- Mandatory first-login password change.
- Accessible password visibility controls.
- Player and admin route guards.
- Responsive desktop sidebar and iPhone bottom navigation.
- Connected dashboard, lobby setup, profile, player explorer, and admin routes.
- Admin-created accounts with transactional Brevo invitations.
- Admin activate/deactivate account lifecycle; deactivated users are rejected at sign-in and when creating draft rooms.
- A searchable FC 26 men's player snapshot with progressive rendering.
- Swagger UI with Bearer authentication at `/swagger`.
- Installable PWA manifest and service worker.

The current identity service is intentionally in-memory for the foundation slice. SQL Server persistence and a durable email outbox remain future backend work.

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
| Admin | `mdevansh@gmail.com` | `Dv@241429` |
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

## Production bundle

Publishing the API in Release mode runs `npm ci` and `npm run build`, then copies the PWA into the API publish output's `wwwroot` directory:

```bash
dotnet publish src/FcDraft.API/FcDraft.API.csproj -c Release -o publish
```

ASP.NET serves the SPA and API from one origin, with SPA fallback excluded for `/api`, `/swagger`, and `/health` paths.
