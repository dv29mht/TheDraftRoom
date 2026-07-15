export type UserRole = 'admin' | 'player'

export type AuthUser = {
  id: string
  displayName: string
  email: string
  role: UserRole
}

export type AuthResponse = {
  accessToken: string
  expiresAt: string
  mustChangePassword: boolean
  user: AuthUser
}

export type ProblemDetails = {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}
