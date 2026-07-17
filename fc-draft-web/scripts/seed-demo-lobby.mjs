#!/usr/bin/env node
// Seeds a demo lobby for device sessions and beta rehearsals (PR-23).
//
// Drives the real REST API: signs in as the host, resolves the invitees from the invitable-users
// directory, and creates a named lobby with everyone invited — so phones/desktops only have to
// sign in, open the lobby, and confirm presence.
//
//   DRAFT_BASE_URL=http://127.0.0.1:5089 node scripts/seed-demo-lobby.mjs
//
// Configuration (environment variables):
//   DRAFT_BASE_URL   target origin (default http://127.0.0.1:5089 — the Testing-env API;
//                    NEVER point manual runs at a Development-environment API: its gitignored
//                    settings hold a real Brevo key and the in-memory branch emails inline)
//   DRAFT_HOST_EMAIL / DRAFT_HOST_PASSWORD
//                    host credentials (default player@draftroom.dev / Player@2026)
//   DRAFT_INVITEES   comma-separated emails (default the three seeded demo players —
//                    requires Database__SeedDemoAccounts=true on the target)
//   DRAFT_NAME       lobby name (default "Beta device session")
//   DRAFT_FORMAT     1v1 | 2v2 (default 2v2)

const base = process.env.DRAFT_BASE_URL ?? 'http://127.0.0.1:5089'
const hostEmail = process.env.DRAFT_HOST_EMAIL ?? 'player@draftroom.dev'
const hostPassword = process.env.DRAFT_HOST_PASSWORD ?? 'Player@2026'
const invitees = (process.env.DRAFT_INVITEES ??
  'player2@draftroom.dev,player3@draftroom.dev,player4@draftroom.dev')
  .split(',')
  .map((email) => email.trim())
  .filter(Boolean)
const name = process.env.DRAFT_NAME ?? 'Beta device session'
const format = process.env.DRAFT_FORMAT ?? '2v2'

async function req(method, path, token, body) {
  const response = await fetch(`${base}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await response.text()
  if (!response.ok) {
    throw new Error(`${method} ${path} -> ${response.status}: ${text.slice(0, 300)}`)
  }
  return text ? JSON.parse(text) : undefined
}

const health = await req('GET', '/health')
console.log(`target ${base} — ${health.status}, contract ${health.contract}, revision ${health.revision}`)

const auth = await req('POST', '/api/auth/login', undefined, { email: hostEmail, password: hostPassword })
const token = auth.accessToken
console.log(`host: ${auth.user.displayName} <${auth.user.email}>`)

const invitable = await req('GET', '/api/drafts/invitable-users', token)
const inviteUserIds = invitees.map((email) => {
  const match = invitable.find((user) => user.email.toLowerCase() === email.toLowerCase())
  if (!match) {
    throw new Error(
      `${email} is not invitable on ${base} — create/activate the account first ` +
      `(or seed the demo players with Database__SeedDemoAccounts=true).`,
    )
  }
  return match.id
})

const detail = await req('POST', '/api/drafts', token, { name, format, inviteUserIds })
console.log(`\nlobby "${detail.summary.name}" (${detail.summary.format}) created — code ${detail.summary.code}`)
console.log(`participants: 1 host + ${inviteUserIds.length} invited`)
console.log(`\nopen it at: <app origin>/drafts/${detail.summary.id}`)
console.log('each invitee: sign in -> notification/hub card -> Confirm presence')
