import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'prompt',
      includeAssets: ['mark.svg', 'logo-horizontal.svg', 'favicon-32.png', 'apple-touch-icon.png'],
      manifest: {
        name: 'The Draft Room',
        short_name: 'Draft Room',
        description: 'Live team drafting for FC Kick Off tournaments.',
        theme_color: '#f7f7f9',
        background_color: '#f7f7f9',
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
      '/api': 'http://localhost:5088',
      // The live draft hub (PR-17): ws upgrade so the SignalR websocket proxies in dev too.
      '/hubs': { target: 'http://localhost:5088', ws: true },
      '/health': 'http://localhost:5088'
    }
  },
  // `vite preview` serves the REAL production build (service worker included), so give it the same
  // proxy: `npm run preview` against a local API is how the PWA lifecycle is verified end-to-end
  // (PR-22). The Playwright e2e suite reuses this server without a backend for its static checks.
  preview: {
    port: 4173,
    proxy: {
      '/api': 'http://localhost:5088',
      '/hubs': { target: 'http://localhost:5088', ws: true },
      '/health': 'http://localhost:5088'
    }
  }
})
