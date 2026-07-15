import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: 'prompt',
      includeAssets: ['mark.svg', 'logo-horizontal.svg', 'favicon-32.png', 'apple-touch-icon.png'],
      manifest: {
        name: 'The Draft Room',
        short_name: 'Draft Room',
        description: 'Live team drafting for FC Kick Off tournaments.',
        theme_color: '#0b0b0f',
        background_color: '#0b0b0f',
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
        runtimeCaching: []
      }
    })
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5088',
      '/health': 'http://localhost:5088'
    }
  }
})
