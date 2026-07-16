import { useCallback, useEffect, useMemo, useState } from 'react'

export type ShortlistEntry = {
  id: number
  name: string
  overall: number
  positions: string[]
  clubName: string
}

/**
 * The personal shortlist (DRAFT_RULES decision 11): a SOLO planning aid — never shared, never voted on.
 * Persisted client-side in localStorage, keyed per user AND per draft, so two accounts on one device (or
 * one account in two drafts) never see each other's bookmarks. Client-side persistence is the recorded
 * MVP decision: the shortlist has no server-side meaning (the server never trusts it for eligibility),
 * so losing it on a device switch costs a bookmark, not a pick.
 */
export function useShortlist(draftId: string, userId: string | undefined) {
  const storageKey = `fc-draft-shortlist:${userId ?? 'anon'}:${draftId}`

  const read = useCallback((): ShortlistEntry[] => {
    try {
      const raw = localStorage.getItem(storageKey)
      const parsed = raw ? (JSON.parse(raw) as ShortlistEntry[]) : []
      return Array.isArray(parsed) ? parsed : []
    } catch {
      return []
    }
  }, [storageKey])

  const [entries, setEntries] = useState<ShortlistEntry[]>(read)

  // Re-read when the identity/draft (and therefore the key) changes.
  useEffect(() => { setEntries(read()) }, [read])

  const ids = useMemo(() => new Set(entries.map((entry) => entry.id)), [entries])

  const toggle = useCallback((entry: ShortlistEntry) => {
    setEntries((current) => {
      const next = current.some((existing) => existing.id === entry.id)
        ? current.filter((existing) => existing.id !== entry.id)
        : [...current, entry]
      try { localStorage.setItem(storageKey, JSON.stringify(next)) } catch { /* quota — the shortlist stays in memory */ }
      return next
    })
  }, [storageKey])

  return { entries, has: (id: number) => ids.has(id), toggle }
}
