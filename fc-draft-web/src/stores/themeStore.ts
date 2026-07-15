import { create } from 'zustand'
import { persist } from 'zustand/middleware'

export type Theme = 'light' | 'dark'

type ThemeState = {
  theme: Theme
  toggleTheme: () => void
}

export const useThemeStore = create<ThemeState>()(
  persist(
    (set) => ({
      theme: 'light',
      toggleTheme: () => set((state) => ({ theme: state.theme === 'light' ? 'dark' : 'light' }))
    }),
    { name: 'draft-room-theme' }
  )
)

export function applyStoredTheme() {
  const stored = localStorage.getItem('draft-room-theme')
  const theme: Theme = stored?.includes('"dark"') ? 'dark' : 'light'
  document.documentElement.dataset.theme = theme
  document.documentElement.style.colorScheme = theme
}
