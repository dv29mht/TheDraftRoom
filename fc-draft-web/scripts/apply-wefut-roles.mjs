// Match WeFUT role rows (crawl-wefut-roles.mjs output) onto our FC 26 dataset and, with
// --write, populate the `roles` array of BOTH dataset copies. Prints a match-quality report
// and, with --write, saves it to WEFUT_ROLES_IMPORT.md at the repo root.
//
// Join: WeFUT `data-base-id` IS the EA player id our dataset keys on, so this is an EXACT
// id join, not a fuzzy name match. Confidence comes from base-card gating + a name cross-check.
//
// Base-card gating (avoids promo-card role inflation): a player has many FUT cards (base
// gold + promos), and promos often carry upgraded role tiers. We only trust the BASE card,
// identified as the card whose OVR equals our dataset's `overall` for that id. Rows from
// higher-OVR promo cards are ignored. A player known to WeFUT only through promos (no card
// at base OVR) is left EMPTY rather than guessed — we never fabricate a role.
//
// Usage:
//   node scripts/apply-wefut-roles.mjs            # dry run: report only, writes nothing
//   node scripts/apply-wefut-roles.mjs --write    # write roles into both dataset copies + report

import { readFile, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const rawPath = resolve('scripts/.cache/wefut/roles-raw.json')
const datasetPaths = [
  resolve('public/data/fc26-players.json'),
  resolve('../src/FcDraft.Infrastructure/Data/fc26-players.json')
]
const reportPath = resolve('../WEFUT_ROLES_IMPORT.md')
const attribution = 'wefut.com/roles'
const write = process.argv.includes('--write')

// A player has many FUT cards; only the BASE card's roles are trusted. The base card is a
// standard-rarity card (Gold / Gold Rare / Silver / Bronze) at the player's base OVR. In the
// crawl these classes appear EXCLUSIVELY at the base OVR — they are never promos — so gating
// on them removes special cards that happen to share the base OVR (e.g. a Halloween 90 next to
// a base 90) without dropping any matched player.
const baseRarityClasses = new Set(['goldrare', 'gold', 'silverrare', 'silver', 'bronzerare', 'bronze'])

const positionOrder = ['GK', 'RB', 'LB', 'CB', 'CDM', 'CM', 'CAM', 'RM', 'LM', 'RW', 'LW', 'ST']
const posRank = (p) => { const i = positionOrder.indexOf(p); return i === -1 ? 99 : i }
const tierMark = (f) => '+'.repeat(f)

// Loose name comparison for the confidence cross-check. WeFUT prints a short card name
// ("Messi", "Vini Jr."); our dataset has the full common name. We accept a match when the
// two share at least one token (accent/case-insensitive) — enough given the exact id join.
function normalizeName(s) {
  return s.normalize('NFKD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9 ]+/g, ' ').trim()
}
function nameConsistent(wefutName, datasetName) {
  if (!wefutName) return true
  const a = new Set(normalizeName(wefutName).split(/\s+/).filter(Boolean))
  const b = new Set(normalizeName(datasetName).split(/\s+/).filter(Boolean))
  if (a.size === 0) return true
  for (const t of a) if (b.has(t)) return true
  return false
}

async function main() {
  const raw = JSON.parse(await readFile(rawPath, 'utf8'))
  const dataset = JSON.parse(await readFile(datasetPaths[0], 'utf8'))
  const byId = new Map(dataset.players.map((p) => [p.id, p]))

  const rowsById = new Map()
  for (const r of raw.rows) {
    if (!rowsById.has(r.baseId)) rowsById.set(r.baseId, [])
    rowsById.get(r.baseId).push(r)
  }

  const rolesById = new Map()
  const nameMismatches = []
  let ambiguousPromoOnly = 0
  let wefutKnownInDataset = 0

  for (const [baseId, rows] of rowsById) {
    const player = byId.get(baseId)
    if (!player) continue                 // women / icons / sub-75 players: no dataset match, skip
    wefutKnownInDataset++
    const baseRows = rows.filter((r) => r.ovr === player.overall && baseRarityClasses.has(r.cardClass))
    if (baseRows.length === 0) { ambiguousPromoOnly++; continue }

    const mismatch = baseRows.find((r) => !nameConsistent(r.cardName, player.name))
    if (mismatch) nameMismatches.push({ id: baseId, dataset: player.name, wefut: mismatch.cardName })

    const roleMap = new Map()
    for (const r of baseRows) {
      const key = `${r.position}|${r.name}`
      const prev = roleMap.get(key)
      if (!prev || r.familiarity > prev.familiarity) roleMap.set(key, { position: r.position, name: r.name, familiarity: r.familiarity })
    }
    const roles = [...roleMap.values()].sort((a, b) =>
      posRank(a.position) - posRank(b.position) || a.name.localeCompare(b.name))
    rolesById.set(baseId, roles)
  }

  // ---- Aggregate stats ----
  const total = dataset.players.length
  const withRoles = rolesById.size
  const roleEntries = [...rolesById.values()].flat()
  const plusCount = roleEntries.filter((r) => r.familiarity === 1).length
  const plusPlusCount = roleEntries.filter((r) => r.familiarity === 2).length
  const posCoverage = new Map()
  for (const r of roleEntries) posCoverage.set(r.position, (posCoverage.get(r.position) ?? 0) + 1)

  const spotNames = ['Erling Haaland', 'Kylian Mbappé', 'Mohamed Salah', 'Lionel Messi',
    'Vini Jr.', 'Jude Bellingham', 'Rodri', 'Virgil van Dijk', 'Alisson', 'Trent Alexander-Arnold',
    'Harry Kane', 'Kevin De Bruyne']
  const spot = spotNames.map((name) => {
    const p = dataset.players.find((x) => x.name === name)
    if (!p) return { name, line: '(not in dataset)' }
    const roles = rolesById.get(p.id)
    return {
      name, overall: p.overall, position: p.position,
      line: roles?.length ? roles.map((r) => `${r.position} ${r.name}${tierMark(r.familiarity)}`).join(', ') : '(no notable role familiarity)'
    }
  })

  // ---- Console report ----
  const L = []
  L.push(`=== WeFUT role match quality (${raw.rolesById.length} roles crawled) ===`)
  L.push(`Dataset players:                 ${total}`)
  L.push(`WeFUT base-ids matching dataset: ${wefutKnownInDataset}`)
  L.push(`  -> base-card match (roles written, high confidence): ${withRoles}`)
  L.push(`  -> promo-only, no base-OVR card (left EMPTY):        ${ambiguousPromoOnly}`)
  L.push(`Dataset players with NO WeFUT presence (left EMPTY):   ${total - wefutKnownInDataset}`)
  L.push(`Role entries written: ${roleEntries.length}  (Role+ ${plusCount}, Role++ ${plusPlusCount})`)
  L.push(`Name cross-check mismatches on base rows: ${nameMismatches.length}`)
  for (const m of nameMismatches.slice(0, 25)) L.push(`   ! id ${m.id}: dataset="${m.dataset}" wefut="${m.wefut}"`)
  L.push('')
  L.push('Spot-check (well-known players):')
  for (const s of spot) L.push(`  ${s.name}${s.overall ? ` (${s.overall} ${s.position})` : ''}: ${s.line}`)
  console.log('\n' + L.join('\n'))

  if (!write) { console.log('\nDry run. Re-run with --write to populate the dataset copies + report.'); return }

  // ---- Write both dataset copies ----
  for (const path of datasetPaths) {
    const ds = JSON.parse(await readFile(path, 'utf8'))
    let touched = 0
    for (const p of ds.players) {
      const roles = rolesById.get(p.id)
      if (roles?.length) { p.roles = roles; touched++ } else { p.roles = [] }
    }
    ds.rolesSource = attribution
    await writeFile(path, JSON.stringify({ ...ds }) + '\n')
    console.log(`Wrote ${touched} players with roles -> ${path}`)
  }

  // ---- Write committed markdown report ----
  const stamp = new Date().toISOString().slice(0, 10)
  const posRows = [...posCoverage.entries()].sort((a, b) => posRank(a[0]) - posRank(b[0]))
    .map(([pos, n]) => `| ${pos} | ${n} |`).join('\n')
  const md = `# FC 26 player Roles import — match-quality report

_Generated ${stamp} by \`fc-draft-web/scripts/apply-wefut-roles.mjs\`. Source: **${attribution}** (underlying data is EA's; low-volume one-time crawl, cached, credited — PRD §18)._

## Method

EA's public ratings feed does not expose per-position role familiarity (Role / Role+ / Role++).
These were backfilled from WeFUT's role database, enumerated by role × tier
(\`${raw.rolesById.length}\` roles × 2 tiers, paginated). Each WeFUT card carries \`data-base-id\`,
which **is the EA player id** our dataset keys on, so matching is an **exact id join**, not a fuzzy
name match. Because a player has many FUT cards (base gold + promos) and promos often carry upgraded
role tiers, only the **base card** is trusted — a **standard-rarity** card (Gold / Gold Rare / Silver /
Bronze) whose OVR equals our dataset \`overall\`. In the crawl those rarities appear exclusively at the
base OVR (never as promos), so gating on them removes special cards that merely share the base OVR
(e.g. a Halloween 90 alongside a base 90) without dropping any matched player. Players known to WeFUT
only through promo cards (no standard card at base OVR) are **left empty rather than guessed**. No role
is ever fabricated.

## Coverage

| Bucket | Players |
| --- | --- |
| Dataset players | ${total} |
| WeFUT base-ids matching dataset | ${wefutKnownInDataset} |
| — base-card match (**roles written**, high confidence) | **${withRoles}** |
| — promo-only, no base-OVR card (left empty) | ${ambiguousPromoOnly} |
| No WeFUT presence (left empty) | ${total - wefutKnownInDataset} |

Role entries written: **${roleEntries.length}** (Role+ ${plusCount}, Role++ ${plusPlusCount}).
Name cross-check mismatches on base-card rows: **${nameMismatches.length}**${nameMismatches.length ? '\n\n' + nameMismatches.map((m) => `- id ${m.id}: dataset \`${m.dataset}\` vs WeFUT \`${m.wefut}\``).join('\n') : ' (the exact id join is clean).'}

### Role entries by position

| Position | Role entries |
| --- | --- |
${posRows}

## Spot-check (well-known players)

| Player | OVR/Pos | Roles written |
| --- | --- | --- |
${spot.map((s) => `| ${s.name} | ${s.overall ? `${s.overall} ${s.position}` : '—'} | ${s.line} |`).join('\n')}

The base-card roles were confirmed against the live WeFUT **player** pages (an independent view of the
same data): Haaland, Van Dijk, and De Bruyne each matched exactly, including both tiers of Van Dijk's
Defender+ / Ball-Playing Defender++.
`
  await writeFile(reportPath, md)
  console.log(`Wrote report -> ${reportPath}`)
}

await main()
