import type { UserRole } from './auth'

export type ManagedUser = {
  id: string
  displayName: string
  email: string
  role: UserRole
  status: 'active' | 'deactivated'
  mustChangePassword: boolean
  invitationSentAt: string | null
  createdAt: string
}

export type CreateUserInput = {
  displayName: string
  email: string
}

export type UpdateUserInput = {
  displayName: string
  email: string
  role: UserRole
}

export type PagedUsers = {
  items: ManagedUser[]
  page: number
  pageSize: number
  total: number
  totalPages: number
  invitedCount: number
  activatedCount: number
}

export type AdminNotification = {
  id: string
  type: 'player.joined' | 'room.created'
  title: string
  message: string
  createdAt: string
}

export type DraftRoom = {
  id: string
  code: string
  name: string
  format: '1v1' | '2v2'
  hostUserId: string
  hostName: string
  createdAt: string
}

export type AdminSettingsStatus = {
  environment: string
  storage: string
  emailConfigured: boolean
  senderName: string
  senderEmail: string | null
  loginUrl: string
}
