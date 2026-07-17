import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { API_CONTRACT, CONTRACT_HEADER } from './apiContract'

// PR-22 version handshake: the client bundle and the API must compile the SAME contract number
// and header name. This guard reads the backend source so the two can never drift silently —
// bumping one side without the other fails CI (both suites run from the repository).
// Vitest always runs with fc-draft-web as the working directory (locally and in CI).
const backendSource = readFileSync(
  resolve(process.cwd(), '../src/FcDraft.API/ApiContract.cs'),
  'utf8'
)

describe('client/API contract handshake', () => {
  it('compiles the same contract number as the API', () => {
    const backendVersion = backendSource.match(/const int Version = (\d+);/)
    expect(backendVersion).not.toBeNull()
    expect(Number(backendVersion![1])).toBe(API_CONTRACT)
  })

  it('watches the same response header the API stamps', () => {
    const backendHeader = backendSource.match(/const string HeaderName = "([^"]+)";/)
    expect(backendHeader).not.toBeNull()
    // axios lowercases response header names; the wire format is case-insensitive.
    expect(backendHeader![1].toLowerCase()).toBe(CONTRACT_HEADER)
  })
})
