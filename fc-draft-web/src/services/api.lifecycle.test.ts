import { AxiosError, AxiosHeaders } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api, getApiError, isOfflineError, OfflineError } from './api'
import { registerServiceWorkerHooks, useAppLifecycleStore } from '../stores/appLifecycleStore'

/* eslint-disable @typescript-eslint/no-explicit-any */
const requestFulfilled = (api.interceptors.request as any).handlers[0].fulfilled as (config: {
  method?: string
  headers: Record<string, unknown>
}) => { headers: Record<string, unknown> }
const responseFulfilled = (api.interceptors.response as any).handlers[0].fulfilled as (response: {
  headers: Record<string, string>
}) => unknown
/* eslint-enable @typescript-eslint/no-explicit-any */

function resetLifecycle() {
  useAppLifecycleStore.setState({
    online: true,
    updateReady: false,
    updateDismissed: false,
    installPrompt: null
  })
}

describe('offline mutation blocking (PR-22 §12.2)', () => {
  beforeEach(resetLifecycle)

  it('rejects a mutation instantly while offline, before anything is sent', () => {
    useAppLifecycleStore.setState({ online: false })
    expect(() => requestFulfilled({ method: 'post', headers: {} })).toThrow(OfflineError)
  })

  it.each(['put', 'patch', 'delete'])('blocks %s while offline', (method) => {
    useAppLifecycleStore.setState({ online: false })
    expect(() => requestFulfilled({ method, headers: {} })).toThrow(OfflineError)
  })

  it('lets reads pass while offline', () => {
    useAppLifecycleStore.setState({ online: false })
    expect(() => requestFulfilled({ method: 'get', headers: {} })).not.toThrow()
  })

  it('lets mutations pass while online', () => {
    expect(() => requestFulfilled({ method: 'post', headers: {} })).not.toThrow()
  })

  it('explains the block in plain language', () => {
    const error = new OfflineError()
    expect(isOfflineError(error)).toBe(true)
    expect(getApiError(error)).toBe("You're offline. Reconnect to continue — nothing was sent.")
  })

  it('explains an unreachable server distinctly from a server error', () => {
    const noResponse = new AxiosError('Network Error', 'ERR_NETWORK', {
      headers: new AxiosHeaders()
    } as never)
    expect(getApiError(noResponse)).toBe(
      "Can't reach The Draft Room right now. Check your connection and try again."
    )
  })
})

describe('version handshake (PR-22 §12.2, §18 stale-shell risk)', () => {
  beforeEach(resetLifecycle)

  it('raises the update prompt when the API reports a newer contract', () => {
    const checkForUpdate = vi.fn()
    registerServiceWorkerHooks({ checkForUpdate })

    responseFulfilled({ headers: { 'x-draftroom-contract': '99' } })

    expect(useAppLifecycleStore.getState().updateReady).toBe(true)
    // The mismatch also nudges the service worker to download the replacement shell.
    expect(checkForUpdate).toHaveBeenCalled()
  })

  it('stays quiet when the contract matches', () => {
    responseFulfilled({ headers: { 'x-draftroom-contract': '1' } })
    expect(useAppLifecycleStore.getState().updateReady).toBe(false)
  })

  it('stays quiet when the header is absent (non-API or older deploy)', () => {
    responseFulfilled({ headers: {} })
    expect(useAppLifecycleStore.getState().updateReady).toBe(false)
  })

  it('re-raises a dismissed prompt on the next mismatch', () => {
    useAppLifecycleStore.setState({ updateReady: true, updateDismissed: true })
    useAppLifecycleStore.setState({ updateReady: false })
    responseFulfilled({ headers: { 'x-draftroom-contract': '99' } })
    const state = useAppLifecycleStore.getState()
    expect(state.updateReady).toBe(true)
    expect(state.updateDismissed).toBe(false)
  })
})
