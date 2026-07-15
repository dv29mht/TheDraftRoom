import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// Dedicated config for component tests. Vitest prefers this over vite.config.ts, so the
// PWA/service-worker plugin never runs under jsdom. Playwright specs live in e2e/ and use
// their own runner, so they are excluded here.
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    css: false,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    exclude: ['e2e/**', 'node_modules/**', 'dist/**'],
  },
})
