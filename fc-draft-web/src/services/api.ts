import axios from 'axios'
import type { AuthResponse, ProblemDetails } from '../types/auth'
import type { AdminNotification, AdminSettingsStatus, Club, CreateUserInput, DatasetImportReport, DatasetVersion, DatasetVersionDetail, ManagedUser, PagedUsers, RosterTemplateDetail, RosterTemplateSummary, UpdateUserInput } from '../types/admin'
import type { CreateLobbyInput, DraftBoard, DraftDetail, DraftSeed, DraftSummary, InvitableUser, TeamFormationInput } from '../types/draft'
import type { PlayerFilterOptions, PlayerSearchParams, PlayerSearchResult } from '../data/fc26Players'

export const api = axios.create({ baseURL: '/api', timeout: 12_000 })

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('fc-draft-token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// When a stored session is rejected (token revoked by a password change/reset, deactivation, or
// sign-out-everywhere), clear it and return to sign-in. Login/reset endpoints legitimately return
// 401 without a session, so they are excluded to avoid hijacking their own error handling.
api.interceptors.response.use(
  (response) => response,
  (error) => {
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
  board: async (draftId: string, clubId?: string) => {
    const { data } = await api.get<DraftBoard>(`/drafts/${draftId}/board`, { params: clubId ? { clubId } : {} })
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

export function getApiError(error: unknown): string {
  if (axios.isAxiosError<ProblemDetails>(error)) {
    const validation = error.response?.data?.errors
    const firstValidation = validation && Object.values(validation).flat()[0]
    return firstValidation ?? error.response?.data?.detail ?? 'The server could not complete that request.'
  }
  return 'Something went wrong. Please try again.'
}
