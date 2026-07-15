import axios from 'axios'
import type { AuthResponse, ProblemDetails } from '../types/auth'
import type { AdminNotification, AdminSettingsStatus, CreateUserInput, DraftRoom, ManagedUser, PagedUsers, UpdateUserInput } from '../types/admin'

export const api = axios.create({ baseURL: '/api', timeout: 12_000 })

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('fc-draft-token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

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
  },
  remove: async (userId: string) => {
    await api.delete(`/users/${userId}`)
  }
}

export const notificationsApi = {
  list: async () => {
    const { data } = await api.get<AdminNotification[]>('/notifications')
    return data
  }
}

export const draftRoomsApi = {
  list: async () => {
    const { data } = await api.get<DraftRoom[]>('/draft-rooms')
    return data
  },
  create: async (input: { name: string; format: '1v1' | '2v2' }) => {
    const { data } = await api.post<DraftRoom>('/draft-rooms', input)
    return data
  }
}

export const adminSettingsApi = {
  get: async () => {
    const { data } = await api.get<AdminSettingsStatus>('/admin/settings')
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
