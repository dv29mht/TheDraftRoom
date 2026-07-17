/**
 * The compiled-in client/API compatibility contract (PR-22, PRD §12.2).
 *
 * MUST equal `ApiContract.Version` in `src/FcDraft.API/ApiContract.cs` — a vitest guard
 * (apiContract.test.ts) reads the C# file and fails the build when the two drift. The API stamps
 * its number on every /api response; a mismatch means this bundle is a stale service-worker shell
 * running against a newer deploy, and the app prompts for the waiting update (§18 risk: "cached
 * PWA shell becomes incompatible with API"). Bump ONLY on a breaking API change.
 */
export const API_CONTRACT = 1

/** Response header carrying the server's contract number (axios lowercases header names). */
export const CONTRACT_HEADER = 'x-draftroom-contract'
