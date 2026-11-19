import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import vueDevTools from 'vite-plugin-vue-devtools'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    tailwindcss(),
    vueDevTools(),
    VitePWA({
      // The app shows its own "new version" prompt (shared/pwa/register.ts).
      registerType: 'prompt',
      injectRegister: false,
      includeAssets: ['favicon.svg', 'icons/apple-touch-icon-180.png'],
      manifest: {
        name: 'iTask',
        short_name: 'iTask',
        description: 'Boards, lists and cards for your tasks.',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        theme_color: '#1D5BF5',
        background_color: '#E9EEF6',
        icons: [
          { src: '/icons/pwa-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icons/pwa-512.png', sizes: '512x512', type: 'image/png' },
          {
            src: '/icons/maskable-512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable',
          },
        ],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,png,woff2}'],
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//],
        cleanupOutdatedCaches: true,
        // The first install takes over the open tab, so boards viewed on the first visit
        // are cached too. Later versions still wait for the update prompt.
        clientsClaim: true,
        runtimeCaching: [
          {
            // Boards the user has opened (and the session check the router needs) stay
            // readable offline. Network first, so online reads are never stale. The API
            // may live on another origin, so only the path is matched. Attachment links
            // are short-lived signed URLs and are not worth keeping.
            urlPattern: ({ url, request }) =>
              request.method === 'GET' &&
              (url.pathname === '/api/me' ||
                (url.pathname.startsWith('/api/boards') &&
                  !/\/attachments\/[^/]+\/url$/.test(url.pathname))),
            handler: 'NetworkFirst',
            options: {
              cacheName: 'itask-api',
              expiration: { maxAgeSeconds: 24 * 60 * 60, maxEntries: 300 },
              cacheableResponse: { statuses: [200] },
              plugins: [
                {
                  // NetworkFirst reads the cache only when the network failed: tag those
                  // answers so the app knows it shows saved data (shared/pwa/network.ts).
                  cachedResponseWillBeUsed: async ({ cachedResponse }) => {
                    if (!cachedResponse) return null
                    const headers = new Headers(cachedResponse.headers)
                    headers.set('X-ITask-Offline', '1')
                    headers.set('Access-Control-Expose-Headers', 'X-ITask-Offline')
                    return new Response(await cachedResponse.blob(), {
                      status: cachedResponse.status,
                      statusText: cachedResponse.statusText,
                      headers,
                    })
                  },
                },
              ],
            },
          },
        ],
      },
    }),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    // Same-origin in development: the browser talks to Vite, Vite forwards /api
    // to the .NET API, so the auth cookie needs no CORS tricks.
    proxy: {
      '/api': {
        target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5080',
        changeOrigin: true,
        // The live board hub (SignalR) upgrades to WebSockets.
        ws: true,
      },
    },
  },
})
