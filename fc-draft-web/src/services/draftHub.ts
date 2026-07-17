import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import type { DraftDetail, DraftUpdate } from '../types/draft'

// The live draft channel (PR-17). One connection per open lobby page, joined to the draft's server-side
// group; the server pushes a version-stamped `DraftUpdated` envelope after every accepted mutation, timer
// auto-pick, and host control. SignalR AUGMENTS the REST reads — the REST snapshot stays authoritative,
// the page keeps its refresh/poll fallback, and every reconnect re-joins the group and reconciles from a
// fresh snapshot so no action is ever duplicated.

export type DraftHubStatus = 'connecting' | 'connected' | 'reconnecting' | 'offline'

export type DraftHubCallbacks = {
  /** An accepted mutation reached us; `update.detail` is the authoritative snapshot (or null → refetch). */
  onUpdate: (update: DraftUpdate) => void
  /** Fired after every (re)join with the authoritative snapshot returned by the server. */
  onSnapshot: (detail: DraftDetail) => void
  onStatusChange: (status: DraftHubStatus) => void
}

export type DraftHubHandle = {
  stop: () => Promise<void>
}

/**
 * Connects to /hubs/draft, joins the draft's group, and keeps the subscription alive across automatic
 * reconnects (rejoin + snapshot reconciliation on every reconnect). Returns a handle to tear the
 * connection down when the page unmounts.
 */
export function connectDraftHub(draftId: string, callbacks: DraftHubCallbacks): DraftHubHandle {
  const token = localStorage.getItem('fc-draft-token') ?? ''
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl('/hubs/draft', { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .build()

  let stopped = false

  const join = async (reconnect = false) => {
    // Joining returns the authoritative snapshot, so a (re)connect reconciles state and version in the
    // same round-trip — never by replaying actions. A rejoin after a dropped connection uses the
    // dedicated RejoinDraft method (identical behaviour) so the server's §15 reconnection-success
    // metric can tell a recovered session from a first join.
    const detail = await connection.invoke<DraftDetail>(reconnect ? 'RejoinDraft' : 'JoinDraft', draftId)
    callbacks.onSnapshot(detail)
  }

  connection.on('DraftUpdated', (update: DraftUpdate) => callbacks.onUpdate(update))

  connection.onreconnecting(() => callbacks.onStatusChange('reconnecting'))
  connection.onreconnected(() => {
    callbacks.onStatusChange('connected')
    void join(true).catch(() => callbacks.onStatusChange('offline'))
  })
  connection.onclose(() => {
    if (!stopped) callbacks.onStatusChange('offline')
  })

  callbacks.onStatusChange('connecting')
  void connection
    .start()
    .then(async () => {
      callbacks.onStatusChange('connected')
      await join()
    })
    .catch(() => callbacks.onStatusChange('offline'))

  return {
    stop: async () => {
      stopped = true
      if (connection.state !== HubConnectionState.Disconnected) {
        await connection.stop().catch(() => undefined)
      }
    },
  }
}
