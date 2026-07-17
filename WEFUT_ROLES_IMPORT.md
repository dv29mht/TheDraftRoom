# FC 26 player Roles import — match-quality report

_Generated 2026-07-17 by `fc-draft-web/scripts/apply-wefut-roles.mjs`. Source: **wefut.com/roles** (underlying data is EA's; low-volume one-time crawl, cached, credited — PRD §18)._

## Method

EA's public ratings feed does not expose per-position role familiarity (Role / Role+ / Role++).
These were backfilled from WeFUT's role database, enumerated by role × tier
(`49` roles × 2 tiers, paginated). Each WeFUT card carries `data-base-id`,
which **is the EA player id** our dataset keys on, so matching is an **exact id join**, not a fuzzy
name match. Because a player has many FUT cards (base gold + promos) and promos often carry upgraded
role tiers, only the **base card** is trusted — a **standard-rarity** card (Gold / Gold Rare / Silver /
Bronze) whose OVR equals our dataset `overall`. In the crawl those rarities appear exclusively at the
base OVR (never as promos), so gating on them removes special cards that merely share the base OVR
(e.g. a Halloween 90 alongside a base 90) without dropping any matched player. Players known to WeFUT
only through promo cards (no standard card at base OVR) are **left empty rather than guessed**. No role
is ever fabricated.

## Coverage

| Bucket | Players |
| --- | --- |
| Dataset players | 1748 |
| WeFUT base-ids matching dataset | 1728 |
| — base-card match (**roles written**, high confidence) | **1716** |
| — promo-only, no base-OVR card (left empty) | 12 |
| No WeFUT presence (left empty) | 20 |

Role entries written: **3907** (Role+ 3756, Role++ 151).
Name cross-check mismatches on base-card rows: **0** (the exact id join is clean).

### Role entries by position

| Position | Role entries |
| --- | --- |
| GK | 171 |
| RB | 137 |
| LB | 174 |
| CB | 685 |
| CDM | 466 |
| CM | 560 |
| CAM | 428 |
| RM | 241 |
| LM | 325 |
| RW | 160 |
| LW | 169 |
| ST | 391 |

## Spot-check (well-known players)

| Player | OVR/Pos | Roles written |
| --- | --- | --- |
| Erling Haaland | 90 ST | ST Advanced Forward++ |
| Kylian Mbappé | 91 ST | LW Inside Forward++, ST Advanced Forward++, ST False 9+ |
| Mohamed Salah | 91 RM | RW Inside Forward++ |
| Lionel Messi | 86 RW | CAM Half Winger+, RW Wide Playmaker++, RW Winger+, ST False 9++ |
| Vini Jr. | 89 LW | LM Inside Forward+, LW Inside Forward++, LW Winger+, ST False 9++ |
| Jude Bellingham | 90 CAM | CM Playmaker++, CAM Half Winger+, CAM Playmaker++ |
| Rodri | 90 CDM | CDM Deep-Lying Playmaker++, CM Playmaker++ |
| Virgil van Dijk | 90 CB | CB Ball-Playing Defender++, CB Defender+ |
| Alisson | 89 GK | GK Ball-Playing Keeper++ |
| Trent Alexander-Arnold | 86 RB | RB Inverted Wingback+ |
| Harry Kane | 89 ST | ST Advanced Forward++ |
| Kevin De Bruyne | 87 CM | CM Half Winger++, CM Playmaker+, CAM Classic 10++ |

The base-card roles were confirmed against the live WeFUT **player** pages (an independent view of the
same data): Haaland, Van Dijk, and De Bruyne each matched exactly, including both tiers of Van Dijk's
Defender+ / Ball-Playing Defender++.
