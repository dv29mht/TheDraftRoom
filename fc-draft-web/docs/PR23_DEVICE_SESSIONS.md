# PR-23 — Real-device session checklist and evidence capture

The §17.9 done-when requires **repeated real-iPhone and desktop sessions completing
successfully**. The operator runs these sessions; this document is the kit — target setup, the
per-session checklist, and the capture template to fill in. Completed records become the manual
half of the [PR23 acceptance matrix](PR23_EVIDENCE.md); screenshots go to
[`assets/pr23-evidence/`](assets/pr23-evidence/) following the PR-22 naming pattern.

## Target setup

**Preferred target: the live deployment** — `https://the-draft-room-909367690008.us-east4.run.app`.
It is the only target with real TLS, so it is the only place the iPhone PWA install, service
worker, and push-free lifecycle behave exactly as shipped.

- Accounts: create the session's players in **Admin → Users** with real inboxes (Brevo delivers
  the one-time password). 1v1 needs 2, 2v2 needs 4. First sign-in exercises the forced password
  change — that IS §16.1 evidence, capture it once.
- Optional pre-seeded lobby: not available against production by script (by design — the seed
  script targets local Testing); create the lobby in the UI as the host instead.

**Local alternative (rehearsal only):** API in environment Testing +
`npm run preview -- --host` (phones on the same Wi-Fi hit `http://<mac-ip>:4173`). Demo accounts
come from `Database__SeedDemoAccounts=true`; a ready lobby comes from
`node scripts/seed-demo-lobby.mjs`. ⚠️ Plain-HTTP LAN origins cannot register the service worker,
so PWA install/update checks are only valid against the live URL. Never use environment
**Development** for manual sessions — its gitignored settings hold a real Brevo key and the
in-memory branch delivers email inline.

## Session matrix (minimum for the done-when)

| Session | Format | Devices | Focus |
|---|---|---|---|
| A | 1v1 | iPhone (installed PWA) + desktop Chrome | Full journey, install, one timer expiry |
| B | 2v2 | iPhone Safari + 3 desktop browsers (4 humans or 4 signed-in windows) | Seeds/pairs, either-teammate picks, reconnect |
| Repeat | either | any | The "repeated … sessions" requirement — at least one re-run of A or B on a later day |

## Per-session checklist

Pre-flight: `/health` → 200 (note `revision`), all participants activated, phone on cellular or
Wi-Fi as planned.

1. **Sign-in** — every participant signs in; any first-time account is forced through the
   password change before reaching the app (§16.1).
2. **Lobby** — host creates the lobby (name/format/template), invites everyone; invite emails +
   in-app notifications arrive (§16.13); participants confirm presence; host sees presence flip
   live (§16.9).
3. **(2v2) Seeds & teams** — host assigns Seed 1/Seed 2 and pairs teams; a wrong pairing is
   blocked; everyone sees teams live (§16.4).
4. **Ready & start** — everyone readies up; only the host has an enabled Start (§16.5).
5. **Spinner** — host spins; every device shows the same committed order (§16.5).
6. **Club round** — each team picks a five-star club + protects a player in order; a taken club
   disappears for the next team (§16.6).
7. **Position draft** — ST first; the 120 s clock runs on every device; player cards show stats,
   alt positions, roles, PlayStyles (§16.7/§16.8); picks propagate to every device without
   refresh (§16.9).
8. **One-handed iPhone drive** — at least five consecutive picks made one-handed on the phone at
   arm's length: search, detail card, confirm sheet, no horizontal scroll, action bar above the
   keyboard (§16.12).
9. **Timer expiry** — one team deliberately lets the clock hit 0; the server auto-picks the best
   eligible player and advances; every device shows the auto-pick (§16.7/§6.4).
10. **Reconnect drill** — mid-draft, the iPhone toggles airplane mode for ~30 s; the app shows
    the offline state, then reconnects and resumes from authoritative state with no duplicate
    pick (§16.11).
11. **Race** — (2v2) both teammates confirm different players near-simultaneously; exactly one
    wins, the loser sees the explanation (§16.10).
12. **Completion & results** — final slot completes the draft on every device; results page shows
    both/all squads in formation + list views; reopening later still works (§16.9/§9.7).
13. **PWA (iPhone, live target, once)** — Safari Share → Add to Home Screen; the installed app
    launches standalone into the signed-in session; after a later deploy, the update prompt
    appears rather than a stale shell (§16.12/§12.2).

## Capture template

Copy one block per session into this file (or a dated sibling), fill it in, and drop screenshots
into `assets/pr23-evidence/` (suggested names: `sessionA-iphone-room.png`,
`sessionA-results.png`, `sessionB-race-loser.png`, …).

```markdown
### Session <A|B|repeat-…> — <date>

- Target: <live URL + /health revision | local rehearsal>
- Format & participants: <1v1/2v2; devices per participant>
- Draft: <lobby name / code / draft id>
- Checklist: 1 ☐  2 ☐  3 ☐  4 ☐  5 ☐  6 ☐  7 ☐  8 ☐  9 ☐  10 ☐  11 ☐  12 ☐  13 ☐
  (mark ✅ / ❌ / n-a; anything ❌ gets a note below)
- Timer-expiry auto-pick observed: <who/slot/player>
- Reconnect drill: <device, offline duration, resumed at pick N, duplicates: none/…>
- Race outcome: <winner/loser message seen>
- Defects found: <none | list with severity — a release-blocker reopens PR-23>
- Screenshots: <filenames>
- Verdict: <complete / incomplete>
```

## Recorded sessions

*(append completed session records here)*
