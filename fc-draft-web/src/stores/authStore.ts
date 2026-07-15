import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { AuthResponse, AuthUser } from '../types/auth'

type AuthState = {
  user: AuthUser | null
  accessToken: string | null
  mustChangePassword: boolean
  setSession: (session: AuthResponse) => void
  logout: () => void
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      accessToken: null,
      mustChangePassword: false,
      setSession: (session) => {
        localStorage.setItem('fc-draft-token', session.accessToken)
        set({
          user: session.user,
          accessToken: session.accessToken,
          mustChangePassword: session.mustChangePassword
        })
      },
      logout: () => {
        localStorage.removeItem('fc-draft-token')
        set({ user: null, accessToken: null, mustChangePassword: false })
      }
    }),
    {
      name: 'fc-draft-auth',
      partialize: (state) => ({
        user: state.user,
        accessToken: state.accessToken,
        mustChangePassword: state.mustChangePassword
      })
    }
  )
)
