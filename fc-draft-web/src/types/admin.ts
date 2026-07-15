import type { UserRole } from './auth'

export type ManagedUser = {
  id: string
  displayName: string
  email: string
  role: UserRole
  status: 'active' | 'deactivated'
  mustChangePassword: boolean
  avatarUrl: string | null
  preferredTeamName: string | null
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
  avatarUrl: string | null
  preferredTeamName: string | null
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
  type: 'player.joined' | 'draft.created'
  title: string
  message: string
  createdAt: string
}

export type DatasetVersion = {
  id: string
  label: string
  source: string
  status: 'Draft' | 'Active' | 'Archived'
  footballerCount: number
  clubCount: number
  errorCount: number
  warningCount: number
  createdAt: string
  activatedAt: string | null
}

export type DatasetIssue = {
  severity: 'Error' | 'Warning'
  row: number
  externalId: number | null
  field: string | null
  message: string
}

export type DatasetVersionDetail = {
  summary: DatasetVersion
  issues: DatasetIssue[]
}

export type DatasetImportReport = {
  versionId: string
  label: string
  status: string
  rowsTotal: number
  rowsImported: number
  clubCount: number
  errorCount: number
  warningCount: number
  issues: DatasetIssue[]
}

export type RosterSlot = {
  order: number
  slotType: 'Held' | 'StartingPosition' | 'FlexBench'
  position: string | null
  label: string
}

export type RosterTemplateSummary = {
  id: string
  name: string
  isActive: boolean
  pickTimerSeconds: number
  slotCount: number
  createdAt: string
}

export type RosterTemplateDetail = {
  summary: RosterTemplateSummary
  slots: RosterSlot[]
}

export type Club = {
  id: string
  name: string
  league: string
  isFiveStarEligible: boolean
}

export type AdminSettingsStatus = {
  environment: string
  storage: string
  emailConfigured: boolean
  senderName: string
  senderEmail: string | null
  loginUrl: string
}
