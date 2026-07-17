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

// Admin communications (PR-21, §9.8).

export type AnnouncementAudience = 'all' | 'draft'

export type ComposeAnnouncementInput = {
  subject: string
  body: string
  audience: AnnouncementAudience
  draftId: string | null
}

export type AnnouncementPreview = {
  subject: string
  body: string
  audience: AnnouncementAudience
  draftId: string | null
  draftName: string | null
  audienceLabel: string
  recipientCount: number
  emailRecipientCount: number
  optedOutCount: number
}

export type AnnouncementPreviewResponse = {
  preview: AnnouncementPreview
  senderName: string
  senderEmail: string | null
  emailConfigured: boolean
}

export type Announcement = {
  id: string
  subject: string
  body: string
  audience: AnnouncementAudience
  draftId: string | null
  audienceLabel: string
  recipientCount: number
  emailCount: number
  optedOutCount: number
  requestedByUserId: string
  requestedByEmail: string
  requestedAt: string
  emailsPending: number
  emailsSent: number
  emailsFailed: number
}

// Admin audit views (PR-21, §9.10).

export type DraftAuditEvent = {
  draftId: string
  draftName: string
  draftCode: string
  sequence: number
  type: string
  fromStatus: string | null
  toStatus: string | null
  version: number
  actorUserId: string | null
  actorName: string | null
  reason: string | null
  createdAt: string
}

export type SecurityAuditEvent = {
  id: string
  action: string
  userId: string | null
  email: string | null
  detail: string | null
  ipAddress: string | null
  createdAt: string
}

export type DraftAuditFilters = {
  draftId?: string
  type?: string
  actorUserId?: string
  from?: string
  to?: string
  take?: number
}

export type SecurityAuditFilters = {
  action?: string
  userId?: string
  email?: string
  from?: string
  to?: string
  take?: number
}

/** One outbox email's delivery metadata (§9.8 delivery visibility) — never a secret. */
export type EmailOutboxItem = {
  id: string
  kind: string
  toEmail: string
  status: 'Pending' | 'Sent' | 'Failed'
  attemptCount: number
  lastError: string | null
  createdAt: string
  sentAt: string | null
  nextAttemptAt: string
  campaignId: string | null
}
