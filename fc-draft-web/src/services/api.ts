import axios from 'axios'
import { API_CONTRACT, CONTRACT_HEADER } from './apiContract'
import { useAppLifecycleStore } from '../stores/appLifecycleStore'
import type { AuthResponse, ProblemDetails } from '../types/auth'
import type { AdminNotification, AdminSettingsStatus, Announcement, AnnouncementPreviewResponse, Club, ComposeAnnouncementInput, CreateUserInput, DatasetImportReport, DatasetVersion, DatasetVersionDetail, DraftAuditEvent, DraftAuditFilters, EmailOutboxItem, ManagedUser, PagedUsers, RosterTemplateDetail, RosterTemplateSummary, SecurityAuditEvent, SecurityAuditFilters, UpdateUserInput } from '../types/admin'
import type { CreateLobbyInput, DraftBoard, DraftBoardParams, DraftDetail, DraftFootballerCard, DraftResults, DraftSeed, DraftSummary, EmailPreferences, InvitableUser, TeamFormationInput, UserNotifications } from '../types/draft'
import type { PlayerFilterOptions, PlayerSearchParams, PlayerSearchResult } from '../data/fc26Players'

export const api = axios.create({ baseURL: '/api', timeout: 12_000 })

/** Raised BEFORE a mutation leaves the device while offline (PR-22, §12.2). */
export class OfflineError extends Error {
  constructor() {
    super("You're offline. Reconnect to continue — nothing was sent.")
    this.name = 'OfflineError'
  }
}

export function isOfflineError(error: unknown): error is OfflineError {
  return error instanceof OfflineError
}

const MUTATING_METHODS = new Set(['post', 'put', 'patch', 'delete'])

api.interceptors.request.use((config) => {
  // Block offline mutations at the seam every write goes through (§12.2: show an offline state
  // rather than allowing offline mutations) — the request fails instantly with a clear message
  // instead of hanging into a confusing network timeout. Reads still pass: a stale-while-offline
  // read can succeed from an intermediary and is harmless.
  if (MUTATING_METHODS.has(config.method ?? '') && !useAppLifecycleStore.getState().online) {
    throw new OfflineError()
  }
  const token = localStorage.getItem('fc-draft-token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// Version handshake (PR-22): every /api response carries the server's contract number. A mismatch
// means this bundle is a stale cached shell running against a newer deploy — raise the refresh
// prompt (which also nudges the service worker to fetch the new shell).
function checkContractHeader(headers: unknown) {
  const value = (headers as Record<string, string> | undefined)?.[CONTRACT_HEADER]
  if (value !== undefined && Number(value) !== API_CONTRACT) {
    useAppLifecycleStore.getState().reportContractMismatch()
  }
}

// When a stored session is rejected (token revoked by a password change/reset, deactivation, or
// sign-out-everywhere), clear it and return to sign-in. Login/reset endpoints legitimately return
// 401 without a session, so they are excluded to avoid hijacking their own error handling.
api.interceptors.response.use(
  (response) => {
    checkContractHeader(response.headers)
    return response
  },
  (error) => {
    if (axios.isAxiosError(error) && error.response) checkContractHeader(error.response.headers)
    const status = axios.isAxiosError(error) ? error.response?.status : undefined
    const url = axios.isAxiosError(error) ? error.config?.url ?? '' : ''
    const authEndpoint = url.includes('/auth/login') || url.includes('/auth/reset-password')
    if (status === 401 && !authEndpoint && localStorage.getItem('fc-draft-token')) {
      localStorage.removeItem('fc-draft-token')
      localStorage.removeItem('fc-draft-auth')
      if (typeof window !== 'undefined' && window.location.pathname !== '/login') {
        window.location.assign('/login')
      }
    }
    return Promise.reject(error)
  }
)

export const authApi = {
  login: async (email: string, password: string) => {
    const { data } = await api.post<AuthResponse>('/auth/login', { email, password })
    return data
  },
  changePassword: async (input: {
    currentPassword: string
    newPassword: string
    confirmPassword: string
  }) => {
    const { data } = await api.post<AuthResponse>('/auth/change-password', input)
    return data
  },
  forgotPassword: async (email: string) => {
    await api.post('/auth/forgot-password', { email })
  },
  resetPassword: async (input: { token: string; newPassword: string; confirmPassword: string }) => {
    const { data } = await api.post<AuthResponse>('/auth/reset-password', input)
    return data
  },
  logoutAll: async () => {
    await api.post('/auth/logout-all')
  }
}

export const usersApi = {
  list: async (params: { page: number; pageSize: number; search?: string }) => {
    const { data } = await api.get<PagedUsers>('/users', { params })
    return data
  },
  create: async (input: CreateUserInput) => {
    const { data } = await api.post<ManagedUser>('/users', input)
    return data
  },
  update: async (userId: string, input: UpdateUserInput) => {
    const { data } = await api.put<ManagedUser>(`/users/${userId}`, input)
    return data
  },
  sendInvite: async (userId: string) => {
    const { data } = await api.post<ManagedUser>(`/users/${userId}/invite`)
    return data
  },
  setStatus: async (userId: string, status: 'active' | 'deactivated') => {
    const action = status === 'active' ? 'activate' : 'deactivate'
    const { data } = await api.post<ManagedUser>(`/users/${userId}/${action}`)
    return data
  }
}

export const notificationsApi = {
  list: async () => {
    const { data } = await api.get<AdminNotification[]>('/notifications')
    return data
  }
}

export const draftsApi = {
  list: async () => {
    const { data } = await api.get<DraftSummary[]>('/drafts')
    return data
  },
  get: async (draftId: string) => {
    const { data } = await api.get<DraftDetail>(`/drafts/${draftId}`)
    return data
  },
  create: async (input: CreateLobbyInput) => {
    const { data } = await api.post<DraftDetail>('/drafts', input)
    return data
  },
  rosterTemplates: async () => {
    const { data } = await api.get<RosterTemplateSummary[]>('/drafts/roster-templates')
    return data
  },
  invitableUsers: async (search?: string) => {
    const { data } = await api.get<InvitableUser[]>('/drafts/invitable-users', { params: search ? { search } : {} })
    return data
  },
  invite: async (draftId: string, inviteUserId: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/invite`, { inviteUserId, expectedVersion })
    return data
  },
  join: async (draftId: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/join`, { expectedVersion })
    return data
  },
  removeParticipant: async (draftId: string, userId: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/participants/${userId}/remove`, { expectedVersion })
    return data
  },
  lock: async (draftId: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/lock`, { expectedVersion })
    return data
  },
  assignSeed: async (draftId: string, participantUserId: string, seed: DraftSeed | null, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/seeds`, { participantUserId, seed, expectedVersion })
    return data
  },
  formTeams: async (draftId: string, teams: TeamFormationInput[] | null, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/teams`, { teams, expectedVersion })
    return data
  },
  setReady: async (draftId: string, ready: boolean, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/ready`, { ready, expectedVersion })
    return data
  },
  beginReadyCheck: async (draftId: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/ready-check`, { expectedVersion })
    return data
  },
  reopenTeams: async (draftId: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/reopen-teams`, { expectedVersion })
    return data
  },
  start: async (draftId: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/start`, { expectedVersion })
    return data
  },
  commitSpinner: async (draftId: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/spinner`, { expectedVersion })
    return data
  },
  openClubSelection: async (draftId: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/open-clubs`, { expectedVersion })
    return data
  },
  selectClubAndProtect: async (draftId: string, clubId: string, footballerId: number, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/club-select`, { clubId, footballerId, expectedVersion })
    return data
  },
  openPositionDraft: async (draftId: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/open-positions`, { expectedVersion })
    return data
  },
  submitPick: async (draftId: string, footballerId: number, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/pick`, { footballerId, expectedVersion })
    return data
  },
  pause: async (draftId: string, reason: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/pause`, { reason, expectedVersion })
    return data
  },
  resume: async (draftId: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/resume`, { expectedVersion })
    return data
  },
  cancel: async (draftId: string, reason: string, expectedVersion: number) => {
    const { data } = await api.post<DraftDetail>(`/drafts/${draftId}/cancel`, { reason, expectedVersion })
    return data
  },
  board: async (draftId: string, params?: DraftBoardParams) => {
    const { data } = await api.get<DraftBoard>(`/drafts/${draftId}/board`, {
      params: {
        ...(params?.clubId ? { clubId: params.clubId } : {}),
        ...(params?.search ? { search: params.search } : {}),
        ...(params?.take ? { take: params.take } : {}),
      },
    })
    return data
  },
  footballerCard: async (draftId: string, footballerId: number) => {
    const { data } = await api.get<DraftFootballerCard>(`/drafts/${draftId}/footballers/${footballerId}`)
    return data
  },
  results: async (draftId: string) => {
    const { data } = await api.get<DraftResults>(`/drafts/${draftId}/results`)
    return data
  },
  remind: async (draftId: string) => {
    const { data } = await api.post<{ reminded: number }>(`/drafts/${draftId}/remind`)
    return data
  }
}

// The caller's own notification centre + email preferences (PR-20, §9.9). Everything is scoped to the
// authenticated user server-side; another user's notification id reads as 404.
export const meApi = {
  notifications: async (params?: { unreadOnly?: boolean; take?: number }) => {
    const { data } = await api.get<UserNotifications>('/me/notifications', { params: params ?? {} })
    return data
  },
  markRead: async (notificationId: string) => {
    const { data } = await api.post<UserNotifications>(`/me/notifications/${notificationId}/read`)
    return data
  },
  markAllRead: async () => {
    const { data } = await api.post<UserNotifications>('/me/notifications/read-all')
    return data
  },
  emailPreferences: async () => {
    const { data } = await api.get<EmailPreferences>('/me/email-preferences')
    return data
  },
  setEmailPreferences: async (preferences: EmailPreferences) => {
    const { data } = await api.put<EmailPreferences>('/me/email-preferences', preferences)
    return data
  }
}

export const adminSettingsApi = {
  get: async () => {
    const { data } = await api.get<AdminSettingsStatus>('/admin/settings')
    return data
  }
}

export const playersApi = {
  search: async (params: PlayerSearchParams) => {
    const { data } = await api.get<PlayerSearchResult>('/players', { params })
    return data
  },
  filters: async () => {
    const { data } = await api.get<PlayerFilterOptions>('/players/filters')
    return data
  }
}

export const datasetsApi = {
  list: async () => {
    const { data } = await api.get<DatasetVersion[]>('/admin/datasets')
    return data
  },
  get: async (versionId: string) => {
    const { data } = await api.get<DatasetVersionDetail>(`/admin/datasets/${versionId}`)
    return data
  },
  importBundled: async () => {
    const { data } = await api.post<DatasetImportReport>('/admin/datasets/import-bundled')
    return data
  },
  activate: async (versionId: string) => {
    const { data } = await api.post<DatasetVersion>(`/admin/datasets/${versionId}/activate`)
    return data
  }
}

export const rosterTemplatesApi = {
  list: async () => {
    const { data } = await api.get<RosterTemplateSummary[]>('/admin/roster-templates')
    return data
  },
  active: async () => {
    const { data } = await api.get<RosterTemplateDetail>('/admin/roster-templates/active')
    return data
  },
  activate: async (templateId: string) => {
    const { data } = await api.post<RosterTemplateSummary>(`/admin/roster-templates/${templateId}/activate`)
    return data
  }
}

export const clubsApi = {
  eligible: async () => {
    const { data } = await api.get<Club[]>('/admin/clubs/eligible')
    return data
  },
  search: async (search: string) => {
    const { data } = await api.get<Club[]>('/admin/clubs', { params: { search } })
    return data
  },
  setFiveStar: async (clubId: string, eligible: boolean) => {
    const { data } = await api.put<Club>(`/admin/clubs/${clubId}/five-star`, { eligible })
    return data
  }
}

// Admin communications (PR-21, §9.8): compose → preview → explicit confirmation → send. The send
// carries the previewed recipient count; the server 409s if the audience moved since the preview.
export const announcementsApi = {
  preview: async (input: ComposeAnnouncementInput) => {
    const { data } = await api.post<AnnouncementPreviewResponse>('/admin/announcements/preview', input)
    return data
  },
  send: async (input: ComposeAnnouncementInput & { confirmedRecipientCount: number }) => {
    const { data } = await api.post<Announcement>('/admin/announcements', input)
    return data
  },
  list: async (take = 50) => {
    const { data } = await api.get<Announcement[]>('/admin/announcements', { params: { take } })
    return data
  }
}

// Admin audit views (PR-21, §9.10): read-only queries over the append-only trails.
export const auditApi = {
  draftEvents: async (filters: DraftAuditFilters = {}) => {
    const { data } = await api.get<DraftAuditEvent[]>('/admin/audit/draft-events', { params: filters })
    return data
  },
  securityEvents: async (filters: SecurityAuditFilters = {}) => {
    const { data } = await api.get<SecurityAuditEvent[]>('/admin/audit/security-events', { params: filters })
    return data
  }
}

// Outbox delivery visibility (§9.8): queued/sent/failed per email — metadata only, never a secret.
export const emailOutboxApi = {
  recent: async (take = 50) => {
    const { data } = await api.get<EmailOutboxItem[]>('/admin/email-outbox', { params: { take } })
    return data
  }
}

export function getApiError(error: unknown): string {
  if (isOfflineError(error)) {
    return error.message
  }
  if (axios.isAxiosError<ProblemDetails>(error)) {
    if (!error.response) {
      // Request left the device but no response arrived (dropped connection, server unreachable).
      return "Can't reach The Draft Room right now. Check your connection and try again."
    }
    const validation = error.response.data?.errors
    const firstValidation = validation && Object.values(validation).flat()[0]
    return firstValidation ?? error.response.data?.detail ?? 'The server could not complete that request.'
  }
  return 'Something went wrong. Please try again.'
}

/** True for an optimistic-concurrency conflict (stale version / audience changed since preview). */
export function isApiConflict(error: unknown): boolean {
  return axios.isAxiosError(error) && error.response?.status === 409
}
