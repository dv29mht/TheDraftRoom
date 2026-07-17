import { mkdir, writeFile, readFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'

const baseUrl = 'https://www.ea.com/games/ea-sports-fc/ratings'
const outputPath = resolve('public/data/fc26-players.json')
const pageSize = 100
const minimumOverall = 75
const concurrency = 8

// Positional Role familiarity (Role+/Role++) is NOT in EA's feed; it is layered on afterwards by
// crawl-wefut-roles.mjs + apply-wefut-roles.mjs. Carry any existing roles forward by EA id so that
// re-importing the EA base data does not wipe that overlay (re-run apply-wefut-roles.mjs to refresh).
async function loadExistingRoles() {
  try {
    const existing = JSON.parse(await readFile(outputPath, 'utf8'))
    return new Map(existing.players.map((player) => [player.id, player.roles ?? []]))
  } catch {
    return new Map()
  }
}
let existingRoles = new Map()

function readNextData(html) {
  const match = html.match(/<script id="__NEXT_DATA__" type="application\/json">(.*?)<\/script>/s)
  if (!match) throw new Error('EA response did not include __NEXT_DATA__.')
  return JSON.parse(match[1])
}

async function fetchPage(page) {
  const response = await fetch(`${baseUrl}?gender=0&page=${page}`, {
    headers: { 'user-agent': 'The Draft Room player dataset importer' }
  })
  if (!response.ok) throw new Error(`EA page ${page} failed with ${response.status}.`)
  const data = readNextData(await response.text())
  return data.props.pageProps.ratingDetails
}

function playerName(player) {
  return player.commonName || [player.firstName, player.lastName].filter(Boolean).join(' ')
}

function slugify(value) {
  return value.normalize('NFKD').replace(/[\u0300-\u036f]/g, '').toLowerCase()
    .replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '')
}

function stat(player, key, label) {
  return { label, value: player.stats?.[key]?.value ?? 0 }
}

function normalize(player) {
  const name = playerName(player)
  const isGoalkeeper = player.position.shortLabel === 'GK'
  return {
    id: player.id,
    name,
    overall: player.overallRating,
    position: player.position.shortLabel,
    alternatePositions: (player.alternatePositions ?? []).map((position) => position.shortLabel),
    club: player.team?.label ?? 'Free agent',
    league: player.leagueName ?? 'Unassigned',
    nation: player.nationality?.label ?? 'Unknown',
    preferredFoot: player.preferredFoot === 1 ? 'Right' : 'Left',
    weakFoot: player.weakFootAbility,
    skillMoves: player.skillMoves,
    height: `${player.height} cm`,
    stats: isGoalkeeper
      ? [stat(player, 'gkDiving', 'DIV'), stat(player, 'gkHandling', 'HAN'), stat(player, 'gkKicking', 'KIC'), stat(player, 'gkReflexes', 'REF'), stat(player, 'sprintSpeed', 'SPD'), stat(player, 'gkPositioning', 'POS')]
      : [stat(player, 'pac', 'PAC'), stat(player, 'sho', 'SHO'), stat(player, 'pas', 'PAS'), stat(player, 'dri', 'DRI'), stat(player, 'def', 'DEF'), stat(player, 'phy', 'PHY')],
    playstyles: (player.playerAbilities ?? []).map((ability) => ({
      name: ability.label.replace(/\+$/, ''),
      plus: ability.type.id === 'playStylePlus'
    })),
    roles: existingRoles.get(player.id) ?? [],
    imageUrl: player.avatarUrl,
    sourceUrl: `https://www.ea.com/games/ea-sports-fc/ratings/player-ratings/${slugify(name)}/${player.id}`
  }
}

async function main() {
  existingRoles = await loadExistingRoles()
  const firstPage = await fetchPage(1)
  const totalPages = Math.ceil(firstPage.totalItems / pageSize)
  const pages = Array.from({ length: totalPages - 1 }, (_, index) => index + 2)
  const batches = []

  for (let index = 0; index < pages.length; index += concurrency) {
    const pageNumbers = pages.slice(index, index + concurrency)
    batches.push(...await Promise.all(pageNumbers.map(fetchPage)))
    process.stdout.write(`\rLoaded ${Math.min(index + concurrency + 1, totalPages)} / ${totalPages} pages`)
  }

  const players = [firstPage, ...batches]
    .flatMap((page) => page.items)
    .filter((player) => player.overallRating >= minimumOverall)
    .map(normalize)
    .sort((left, right) => right.overall - left.overall || left.name.localeCompare(right.name))

  await mkdir(dirname(outputPath), { recursive: true })
  await writeFile(outputPath, `${JSON.stringify({ version: 'FC26-2026-07', source: baseUrl, players })}\n`)
  process.stdout.write(`\nWrote ${players.length} players to ${outputPath}\n`)
}

await main()
