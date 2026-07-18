import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

// Where the dev/preview proxy sends /api, /hubs, and /health. The default is the local API's dev
// port; the full-stack E2E harness (PR-23) points it at its own isolated Testing-environment API
// so it never collides with a developer's running stack.
const apiOrigin = process.env.DRAFT_API_ORIGIN ?? 'http://localhost:5088'

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'prompt',
      includeAssets: ['mark.svg', 'logo-horizontal.svg', 'favicon-32.png', 'apple-touch-icon.png'],
      manifest: {
        name: 'ROSTR',
        short_name: 'ROSTR',
        description: 'Draft. Strategize. Dominate. Live team drafting for FC Kick Off tournaments.',
        theme_color: '#0a0a0a',
        background_color: '#ffffff',
        display: 'standalone',
        start_url: '/',
        icons: [
          { src: '/mark.svg?v=2', sizes: 'any', type: 'image/svg+xml', purpose: 'any' },
          { src: '/pwa-192.png?v=2', sizes: '192x192', type: 'image/png', purpose: 'any maskable' },
          { src: '/pwa-512.png?v=2', sizes: '512x512', type: 'image/png', purpose: 'any maskable' }
        ]
      },
      workbox: {
        navigateFallback: '/index.html',
        // PR-22 (§12.2): the service worker must NEVER answer for the API, the SignalR hub, or
        // health probes — not even the SPA-shell navigation fallback. Combined with the empty
        // runtimeCaching list below, no /api response (authenticated ones especially) is ever
        // cached by the service worker; the API additionally stamps Cache-Control: no-store.
        navigateFallbackDenylist: [/^\/api\//, /^\/hubs\//, /^\/health$/, /^\/swagger/],
        // Include the self-hosted woff2 fonts in the precache (default glob omits them).
        globPatterns: ['**/*.{js,css,html,svg,png,woff2}'],
        runtimeCaching: []
      }
    })
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': apiOrigin,
      // The live draft hub (PR-17): ws upgrade so the SignalR websocket proxies in dev too.
      '/hubs': { target: apiOrigin, ws: true },
      '/health': apiOrigin
    }
  },
  // `vite preview` serves the REAL production build (service worker included), so give it the same
  // proxy: `npm run preview` against a local API is how the PWA lifecycle is verified end-to-end
  // (PR-22). The client-only Playwright suite reuses this server without a backend for its static
  // checks; the full-stack suite (PR-23) boots a Testing-environment API behind it.
  preview: {
    port: 4173,
    proxy: {
      '/api': apiOrigin,
      '/hubs': { target: apiOrigin, ws: true },
      '/health': apiOrigin
    }
  }
})
