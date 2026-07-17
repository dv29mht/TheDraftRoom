// Low-volume, cached, resumable crawler for FC 26 positional Roles from WeFUT.
//
// EA's public ratings feed (import-fc26-players.mjs) does NOT expose per-position role
// familiarity (Role / Role+ / Role++). WeFUT (wefut.com/roles) is the approved secondary
// source for that slice. This script enumerates every role x tier page, paginates each,
// and records the underlying FUT cards. Each card carries `data-base-id`, which is the EA
// player id our dataset keys on — so downstream matching is an EXACT id join, not a fuzzy
// name match. See apply-wefut-roles.mjs for the base-card gating + dataset write.
//
// Data-rights posture (PRD s18): the data is EA's. This crawl is LOW-VOLUME, one-time
// (roles are fixed per game edition), fully cached on disk, resumable, throttled, and the
// source is credited. Re-runs read entirely from cache and hit the network zero times.
//
// Usage:
//   node scripts/crawl-wefut-roles.mjs                 # full crawl (all 49 roles x 2 tiers)
//   node scripts/crawl-wefut-roles.mjs --roles=41,42   # sample: only these plus-tier role ids
//   node scripts/crawl-wefut-roles.mjs --refresh       # ignore cache, refetch

import { mkdir, writeFile, readFile, access } from 'node:fs/promises'
import { resolve } from 'node:path'

const edition = 26
const origin = 'https://wefut.com'
const cacheDir = resolve('scripts/.cache/wefut')
const outputPath = resolve('scripts/.cache/wefut/roles-raw.json')
const userAgent = 'The Draft Room player dataset importer (one-time FC26 role backfill; contact via repo)'
const throttleMs = 1200        // polite delay between NETWORK requests only (cache hits are free)
const pageSize = 40            // WeFUT lists 40 cards per page; offset is a path segment
const minOverall = 75         // our dataset floor; stop paginating a role once cards drop below it

const args = process.argv.slice(2)
const refresh = args.includes('--refresh')
const roleFilter = args.find((a) => a.startsWith('--roles='))
  ?.slice('--roles='.length).split(',').map((s) => Number(s.trim())).filter(Boolean)

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function cachedFetch(pathname) {
  const cacheFile = resolve(cacheDir, pathname.replace(/[^a-z0-9]+/gi, '_') + '.html')
  if (!refresh) {
    try { await access(cacheFile); return { html: await readFile(cacheFile, 'utf8'), cached: true } }
    catch { /* miss */ }
  }
  const response = await fetch(`${origin}${pathname}`, { headers: { 'user-agent': userAgent } })
  if (response.status === 404) return { html: null, cached: false }
  if (!response.ok) throw new Error(`${pathname} failed with ${response.status}`)
  const html = await response.text()
  await mkdir(cacheDir, { recursive: true })
  await writeFile(cacheFile, html)
  await sleep(throttleMs)
  return { html, cached: false }
}

// Parse the /roles index into a catalog: one entry per role (a plus id 1..49) with its
// display name and position, derived from the page's <h3>POSITION</h3> / <h4>Role+</h4> layout.
function parseCatalog(html) {
  const token = /<h3[^>]*>(.*?)<\/h3>|<h4[^>]*>(.*?)<\/h4>|\/roles\/26\/(\d+)\/([a-z0-9-]+)\/(plus|plusplus)/gs
  const stripTags = (s) => s.replace(/<[^>]+>/g, '').replace(/[+]+$/, '').trim()
  const catalog = new Map()
  let position = null
  let name = null
  for (const m of html.matchAll(token)) {
    if (m[1] != null) position = stripTags(m[1])
    else if (m[2] != null) name = stripTags(m[2])
    else {
      const id = Number(m[3])
      if (id > 100) continue // the plusplus link for the same role; we derive it as id+100
      if (!catalog.has(id)) catalog.set(id, { id, slug: m[4], name, position })
    }
  }
  return [...catalog.values()].sort((a, b) => a.id - b.id)
}

// Parse one role page into card rows. Each card block starts at the player anchor and runs
// to the next anchor; within it we read the base id (EA id), card OVR, class, and position.
function parseCards(html) {
  const anchor = /href="https:\/\/wefut\.com\/player\/26\/(\d+)\/([a-z0-9-]+)"><div class="card ([^"]*)" data-base-id="(\d+)"/g
  const matches = [...html.matchAll(anchor)]
  const rows = []
  for (let i = 0; i < matches.length; i++) {
    const m = matches[i]
    const block = html.slice(m.index, i + 1 < matches.length ? matches[i + 1].index : m.index + 3000)
    const ovr = block.match(/<span class="rating"[^>]*>(\d+)<\/span>/)
    const pos = block.match(/<span class="position">([^<]+)<\/span>/)
    const name = block.match(/<span class="marquee">([^<]+)/)
    rows.push({
      cardId: Number(m[1]),
      slug: m[2],
      cardClass: m[3].split(' ')[0],
      baseId: Number(m[4]),
      ovr: ovr ? Number(ovr[1]) : null,
      cardPosition: pos ? pos[1].trim() : null,
      cardName: name ? name[1].trim() : null
    })
  }
  return rows
}

async function crawlRoleTier(role, tier) {
  const id = tier === 'plusplus' ? role.id + 100 : role.id
  const familiarity = tier === 'plusplus' ? 2 : 1
  const rows = []
  for (let offset = 0; ; offset += pageSize) {
    const { html, cached } = await cachedFetch(`/roles/${edition}/${id}/${role.slug}/${tier}/${offset}`)
    if (!html) break
    const cards = parseCards(html)
    if (cards.length === 0) break
    for (const c of cards) {
      rows.push({ baseId: c.baseId, ovr: c.ovr, position: role.position, name: role.name, familiarity, cardClass: c.cardClass, cardName: c.cardName })
    }
    const maxOvr = Math.max(...cards.map((c) => c.ovr ?? 0))
    process.stdout.write(`\r  ${role.position} ${role.name}${'+'.repeat(familiarity)} @${offset}: ${cards.length} cards (top OVR ${maxOvr})${cached ? ' [cache]' : ''}      `)
    if (maxOvr < minOverall || cards.length < pageSize) break
  }
  process.stdout.write('\n')
  return rows
}

async function main() {
  await mkdir(cacheDir, { recursive: true })
  const { html: indexHtml } = await cachedFetch(`/roles`)
  let catalog = parseCatalog(indexHtml)
  if (roleFilter?.length) catalog = catalog.filter((r) => roleFilter.includes(r.id))
  console.log(`Crawling ${catalog.length} roles x 2 tiers from ${origin}/roles ...`)

  const rows = []
  for (const role of catalog) {
    for (const tier of ['plus', 'plusplus']) rows.push(...await crawlRoleTier(role, tier))
  }

  // Dedupe to one row per (baseId, ovr, cardClass, position, name). cardClass MUST be in the key:
  // a base card (e.g. goldrare Defender+) and a same-OVR promo (e.g. globetrotters Defender++) are
  // DIFFERENT cards with the same (position, name) at different tiers. Collapsing on OVR alone would
  // let the promo's higher tier clobber the base row, which the downstream base-rarity gate then drops
  // — silently losing the base card's role. Keeping cardClass preserves each card's own familiarity.
  const byKey = new Map()
  for (const r of rows) {
    const key = `${r.baseId}|${r.ovr}|${r.cardClass}|${r.position}|${r.name}`
    const prev = byKey.get(key)
    if (!prev || r.familiarity > prev.familiarity) byKey.set(key, r)
  }
  const deduped = [...byKey.values()]

  await writeFile(outputPath, JSON.stringify({
    source: `${origin}/roles`,
    edition: `FC${edition}`,
    note: 'Positional role familiarity (Role+/Role++) per FUT card. baseId = EA player id; ovr = that card OVR. Underlying data is EA\'s; sourced from WeFUT.',
    rolesById: catalog.map((r) => ({ id: r.id, name: r.name, position: r.position })),
    rows: deduped
  }) + '\n')

  const players = new Set(deduped.map((r) => r.baseId)).size
  console.log(`\nWrote ${deduped.length} role rows across ${players} distinct players/cards to ${outputPath}`)
}

await main()
