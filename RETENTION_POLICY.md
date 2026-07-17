# Data retention and deletion policy

**Status:** Adopted for the private beta (PR-23, 16 July 2026). This document is the policy PRD
§12.3 requires before production launch. It governs the live deployment (Google Cloud Run
`the-draft-room`, us-east4 + Neon PostgreSQL) and any environment holding real personal data.

The Draft Room is a **private, invite-only community**: every account is created by the sole
administrator for a known person. Personal data held is deliberately minimal — display name,
email address, optional avatar URL and preferred team name, plus activity records (drafts,
picks, notifications, audit trails) attributed to the account.

## 1. What we store, for how long

| Data class | Contents | Retention | Basis / notes |
|---|---|---|---|
| Account profile | Display name, normalized email, role, status, optional avatar/team name | Life of the community; **deactivate-and-retain** (hard delete was removed in PR-04) | Historical attribution of drafts and audit events (§9.2, §9.10) |
| Credentials | BCrypt password hash, security stamp, hashed single-use reset tokens | Hash: life of the account. Reset tokens: single-use, invalid after use/expiry | Never stored or logged in plaintext (§12.3) |
| Draft history | Drafts, participants, teams, picks, append-only `draft_events` | **Indefinite** — append-only by design | §9.7 immutable results; §9.10 audit permanence; the product's core value |
| Security audit trail | Sign-in/failure, resets, revocations, admin actions (actor id/email/IP) | **24 months minimum**, then eligible for archival/purge | Incident investigation window; append-only, no API mutation verb exists |
| Email outbox | Delivery work items + status; **secrets cleared at send** (PR-06); non-secret payload | Delivered/failed rows: **12 months**, then eligible for purge (manual, §3) | Delivery visibility (§9.8) without retaining secrets |
| Announcements | Campaign record (subject/body/audience counts/requester) | Indefinite (append-only admin accountability record, §9.8) | |
| In-app notifications | Per-user notices + read stamps | Life of the account | User-facing history |
| Server logs | Cloud Run request/application logs (correlation ids; never passwords/tokens/email bodies) | **30 days** (Cloud Logging default `_Default` bucket) | Operational debugging |
| Metrics/analytics | Aggregate counters/histograms only (see §15 and `docs/RUNBOOK.md`) | Aggregates only; no per-user series | §15: never passwords, tokens, or email content; tags carry format/outcome, never ids or emails |
| Database backups | Neon point-in-time history of the whole database | Per Neon plan history window (free tier: ~1 day; paid: configurable up to 30 days) | See RUNBOOK backup/recovery |

## 2. Deletion and erasure requests

A community member may ask the administrator to remove their personal data at any time.
Because draft history and audit trails are append-only and shared with other participants,
erasure is implemented as **deactivation plus pseudonymization**, completed within **30 days**
of a verified request:

1. **Deactivate** the account (Admin → Users). The person can no longer sign in; nothing new is
   attributed to them.
2. **Pseudonymize** the profile: replace display name with `Removed player N`, the email with a
   non-routable placeholder (`removed-N@invalid.draftroom`), and clear avatar/preferred team
   name. This preserves referential integrity of drafts, picks, and events while removing the
   personal identifiers. Until an admin UI exists this is an operator SQL step (documented in
   `docs/RUNBOOK.md` §Erasure) run against Neon, inside a transaction.
3. **Outbox/notifications:** delete the subject's `user_notifications` rows and any
   undelivered outbox rows addressed to them; already-delivered outbox rows fall under the
   12-month outbox retention.
4. **Audit trail:** security-audit rows are retained (legitimate interest: they record actions
   *against the system*, and the email column is pseudonymized by the same step). Backups age
   out within the Neon history window; no backup restore may be used to resurrect erased data
   except for disaster recovery, in which case the pseudonymization step is re-applied.

What is **not** deleted: draft results, picks, and events referencing the (pseudonymized)
account — the same rule §9.2 already sets for deactivated users ("historical attribution
remains intact"), now with identifiers removed.

## 3. Purge procedures

No automatic purge job runs in the MVP (single, small community; low volume). The operator
runs the documented purge (RUNBOOK §Retention purges) **quarterly**: delivered/failed outbox
rows older than 12 months and — after the 24-month minimum — security-audit rows older than
the window. Both procedures are plain `DELETE ... WHERE` statements over time columns and are
safe to defer; the policy windows above are the commitment, the cadence is operational.

## 4. Scope notes

- **Environments:** only the live deployment holds real personal data. Local/dev/test
  environments use the seeded deterministic accounts and synthetic data; the committed
  Brevo key lives only in gitignored `appsettings.Development.json` (never in Testing/CI).
- **Processors:** Google Cloud (hosting, logs), Neon (database + backups), Brevo
  (transactional email; message content is the invite/notification templates, delivery
  metadata retained per Brevo's own policy).
- **Review:** revisit this policy before any move beyond the private beta (public traffic,
  more admins, or multi-instance hosting).
