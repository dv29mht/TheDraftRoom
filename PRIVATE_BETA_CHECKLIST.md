# Private-beta launch checklist

The gate between "PR-23 merged" and "friends are drafting". Work through it top to bottom; every
box is either a one-time setup step or a verification against the live deployment
(`https://the-draft-room-909367690008.us-east4.run.app`).

## 1. Platform readiness

- [ ] `main` is green in CI (backend, frontend, both Playwright suites, container build).
- [ ] The deploy workflow ran for the release commit and `/health` returns `200` with the expected
      `contract` and a fresh `revision`.
- [ ] Cloud Run service env is correct per [`DEPLOYMENT.md`](DEPLOYMENT.md) Step 3 — in particular
      `Database__SeedDevelopmentAccounts=false`, `Database__SeedDemoAccounts=false`, a strong
      `Jwt__Key`, and live `Brevo__*` values with a verified sender.
- [ ] `--max-instances 1` still set (single-instance is a hard requirement — RUNBOOK §3).
- [ ] Cloud SQL: automated backups + point-in-time recovery enabled (RUNBOOK §4); the instance is
      attached to the service (`--add-cloudsql-instances`) and the runtime SA holds
      `roles/cloudsql.client`.
- [ ] The seeded admin password has been changed from the repo-public default
      (DEPLOYMENT.md Step 5) and the change was verified by re-login.

## 2. Governance

- [ ] [`RETENTION_POLICY.md`](RETENTION_POLICY.md) reviewed and its erasure/purge procedures
      (RUNBOOK §6) are runnable by the operator.
- [ ] Beta players told, in the invite message, that drafts/audit history are retained and how to
      request erasure (RETENTION_POLICY §2).

## 3. Accounts and demo data

- [ ] Real beta accounts created from **Admin → Users** (name + email are mandatory) — at least 4
      activated accounts before scheduling a 2v2 (capacity rules: 1v1 2–10, 2v2 4–16 even).
- [ ] Each invitee received the Brevo invite email, signed in with the one-time password, and was
      forced through the password change (§16.1 — record one as evidence if not yet captured).
- [ ] Optional rehearsal: a throwaway lobby seeded via
      `node fc-draft-web/scripts/seed-demo-lobby.mjs` against a LOCAL Testing stack (never
      production, never Development).

## 4. Session verification (the §16 done-when)

- [ ] The automated acceptance evidence is current —
      [`fc-draft-web/docs/PR23_EVIDENCE.md`](fc-draft-web/docs/PR23_EVIDENCE.md).
- [ ] Repeated real-device sessions recorded per
      [`fc-draft-web/docs/PR23_DEVICE_SESSIONS.md`](fc-draft-web/docs/PR23_DEVICE_SESSIONS.md):
      at least one full 1v1 AND one full 2v2, each with a real iPhone participant and a desktop
      participant, completing lobby → draft → results.
- [ ] The iPhone session included: PWA install (Safari Share → Add to Home Screen), a mid-draft
      reconnect (airplane-mode toggle), and one 120 s timer expiry auto-pick.
- [ ] No release-blocking defect is open from those sessions.

## 5. Operations on standby

- [ ] Operator has RUNBOOK §1–§6 to hand (deploy verification, rollback, stuck-draft recovery,
      announcement flow).
- [ ] A dry-run of admin draft operations was done on a rehearsal lobby: pause with reason →
      resume → inspect event history in **Admin → Drafts**.
- [ ] Email delivery visibility checked: **Admin → Communications** tallies and
      `GET /api/admin/email-outbox` show the invite sends.
- [ ] Cloud Logging opened once and filtered by a request's `X-Correlation-Id` (so the first
      incident isn't the first attempt).

## 6. Go

- [ ] Beta group invited (draft invitation emails deliver, deep links open the lobby).
- [ ] First scheduled session completed; results page shared with the group.
- [ ] Feedback channel agreed (group chat is fine) and the operator is watching
      `draftroom.*` metrics/logs for the first sessions (RUNBOOK §5).
