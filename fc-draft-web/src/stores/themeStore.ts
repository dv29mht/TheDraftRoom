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

/** Matches --color-bg per theme so browser/PWA chrome follows the app. */
const THEME_COLOR: Record<Theme, string> = { light: '#f7f7f9', dark: '#0b0b0f' }

export function applyTheme(theme: Theme) {
  document.documentElement.dataset.theme = theme
  document.documentElement.style.colorScheme = theme
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', THEME_COLOR[theme])
}

export function applyStoredTheme() {
  const stored = localStorage.getItem('draft-room-theme')
  applyTheme(stored?.includes('"dark"') ? 'dark' : 'light')
}
