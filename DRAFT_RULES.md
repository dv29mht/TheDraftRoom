# The Draft Room — Locked Draft Rules (MVP)

**Status:** Locked v1.0 (PR-01)
**Date:** 14 July 2026
**Supersedes:** the "assumptions requiring confirmation" in PRD §5 and the open questions in PRD §19.

This document is the authoritative rules matrix for the MVP draft. It fixes every
decision that changes the draft state machine or database constraints so the
persistent draft model (PR-10) and the pick engine (PR-14–PR-16) can be built
without further product ambiguity. Roster templates remain configurable in
PR-09; the values below are the **default, active MVP template**.

---

## 1. Squad shape

A completed draft squad holds **16 footballers per draft team**:

| Group | Count | Notes |
|---|---:|---|
| Held (protected) player | 1 | Separate, dedicated pitch slot — a 12th squad member, not part of the XI |
| Starting XI | 11 | Fixed **4-3-3** of concrete positions |
| Substitutes | 4 | Flexible — any position |

The held player is drafted from the team's chosen elite club before the
position draft and is shown in its own slot on the pitch view. It is **not** one
of the 11 starters and does **not** fill or skip any formation position.

**Eligible clubs (updated 17 July 2026):** the pre-draft club round offers EA FC 26 men's
Kick Off clubs rated **five stars or 4.5 stars** (previously five-star only). EA's feed omits
club star ratings, so the eligible set is transcribed from EA's official men's club-rating
reveal — 7 five-star + 9 four-and-a-half-star = 16 clubs — and an admin may curate it further.
This still comfortably exceeds the maximum team count (1v1 ≤ 10, 2v2 ≤ 8), so club uniqueness
per lobby holds.

## 2. Roster template and pick sequence

Each draft team fills its slots in this order. The club + held selection is a
single pre-draft round in straight spinner order; the position and bench rounds
use **snake** order (see decision 5).

| Round | Slot | Type | Eligibility |
|---:|---|---|---|
| Pre-draft | Elite club | club | Unique per lobby; eligible 5★ or 4.5★ Kick Off club |
| Pre-draft | Held player | held | 75+ men's footballer **from the chosen club** |
| 1 | ST | starter | primary/alt position = ST |
| 2 | LW | starter | primary/alt position = LW |
| 3 | RW | starter | primary/alt position = RW |
| 4 | CM | starter | primary/alt position = CM |
| 5 | CM | starter | primary/alt position = CM |
| 6 | CM | starter | primary/alt position = CM |
| 7 | LB | starter | primary/alt position = LB |
| 8 | CB | starter | primary/alt position = CB |
| 9 | CB | starter | primary/alt position = CB |
| 10 | RB | starter | primary/alt position = RB |
| 11 | GK | starter | primary/alt position = GK |
| 12 | Sub 1 | flexible | any position |
| 13 | Sub 2 | flexible | any position |
| 14 | Sub 3 | flexible | any position |
| 15 | Sub 4 | flexible | any position |

All slots draw only from **men's base / Kick Off footballers rated 75 or higher**
(PRD §5, §6.3). Excluded content (women, Icons, Heroes, UT specials, custom/
historical cards) never appears.

---

## 3. Decision matrix

| # | Decision | Locked answer |
|---:|---|---|
| 1 | Formation & position order | 4-3-3; sequence `ST → LW → RW → CM → CM → CM → LB → CB → CB → RB → GK`, then 4 flexible subs |
| 2 | Protected/held player | Drafted pre-draft from the chosen club; a separate 12th squad slot outside the XI; globally removed from the pool |
| 3 | Elite club eligibility & uniqueness | Eligible = EA FC 26 men's 5★ or 4.5★ Kick Off club; each eligible club is unique per lobby |
| 4 | Footballer uniqueness | Globally unique within the lobby (held or drafted → unavailable to all) |
| 5 | Round order | **Snake** — order reverses each position/bench round; the pre-draft club/held round uses straight spinner order |
| 6 | 2v2 pick authority | Either teammate may confirm; first valid server-accepted submission wins |
| 7 | Timer expiry (120s) | **Auto-pick** the highest-rated available eligible footballer for the active slot |
| 8 | Host permission | Any active (non-deactivated) user may create/host a lobby |
| 9 | FC 26 data source | EA public FC 26 ratings feed is authoritative; Role/Role++ and PlayStyles supplemented from secondary sources; licensed media deferred until rights are confirmed |
| 10 | Substitutes / flexible slots | 4 flexible (any-position) bench slots; the XI stays concrete positions; no DEF/MID/FWD flex in the XI |
| 11 | 2v2 shortlist / vote | No teammate voting in MVP; shared pick control suffices. The personal shortlist bookmark was delivered in PR-18 as a SOLO planning aid: client-side (localStorage), keyed per user and per draft, never shared and never trusted by the server for eligibility |
| 12 | Odd 1v1 byes | Draft proceeds regardless of parity; the results view flags a bye for the top-ranked participant. The app does not run the bracket |

---

## 4. Acceptance examples

**1v1 draft (3 participants A, B, C).**
Spinner ranks them A, B, C. Pre-draft round (straight): A, then B, then C each
pick a distinct eligible club (5★ or 4.5★) and hold one player from it. Position round 1
(ST): pick order A → B → C. Round 2 (LW) snakes to C → B → A. Rounds continue
through GK, then 4 sub rounds, still snaking. Each team ends with 16 players.

**2v2 draft (Team A = Seed 1 Sam + Seed 2 Alex).**
On Team A's ST turn, either Sam or Alex may submit. Sam submits Haaland at
0:41; Alex's later submission for the same slot is rejected as the slot is
filled. Both teammates share one squad.

**Protected/held player.**
Team A chooses Real Madrid and holds Bellingham. Bellingham appears in Team A's
dedicated held slot and is removed from every team's available pool. Team A
still drafts a full ST … GK plus 4 subs. Real Madrid cannot be chosen by any
other team.

**Global uniqueness.**
Once Mbappé is held or drafted by any team, he no longer appears in any
position or bench pool for any team.

**Snake ordering.**
With ranks 1-2-3, position round 1 order is 1,2,3; round 2 is 3,2,1; round 3 is
1,2,3, and so on through every starter and sub round.

**Timer expiry / auto-pick.**
Team B's RW turn reaches 0:00 with no submission. The server assigns the
highest-rated available RW-eligible footballer (tie-break: overall, then name,
then stable id), records an auto-pick event, and advances to the next turn.

**Odd byes.**
Five 1v1 participants draft normally. The completed-results view marks the
top-ranked participant with a first-round bye; no draft state is affected.

---

## 5. Derived domain constraints (for PR-10 and later)

These constraints follow from the decisions above and should shape the draft
aggregate, schema, and command validation:

- **Squad slots.** A draft snapshots an ordered slot template: 1 `Held`, 11
  `StartingPosition` (concrete position each), 4 `FlexBench` (any position).
  Slots are immutable once the draft starts.
- **Footballer uniqueness.** Unique constraint on `(DraftId, FootballerId)`
  across the held slot **and** all drafted slots. A pick referencing an already
  used footballer fails transactionally.
- **Club uniqueness.** Unique constraint on `(DraftId, FiveStarClubId)` across
  draft teams.
- **Held player eligibility.** The held footballer's club must equal the team's
  chosen elite club and satisfy the 75+ / men's-base rules.
- **Turn order.** Turn sequence is derived from committed spinner ranks via a
  snake function: round `r` (1-indexed) uses ascending rank when `r` is odd and
  descending when `r` is even; the pre-draft club/held round is always
  ascending.
- **Auto-pick determinism.** Expiry selection is a pure function of the current
  available pool and slot eligibility (highest overall → name → id), so it is
  reproducible in tests and after reconnection.
- **Pick authority.** A 2v2 pick command is accepted from either teammate; the
  first submission that passes turn/version/eligibility/uniqueness wins and the
  slot is then closed.

---

## 6. Notes and deferrals

- **Media/licensing (decision 9):** club crests, player photos, and kits require
  rights confirmation and are not bundled. Text and placeholders are used until
  then. This remains tracked as a launch risk in PRD §18.
- **Roster configurability:** these values are the default active template.
  PR-09 makes templates versioned and admin-editable; an in-progress draft keeps
  the template it snapshotted at start.
- **Temporary-password scheme:** resolved in **PR-05** — a **unique one-time
  secret per invite** (not the fixed `Draft@1234`). Brevo emails it, a token
  authenticated with it may reach only the forced password-change flow, and it is
  rate-limited and rotated on re-invite.
