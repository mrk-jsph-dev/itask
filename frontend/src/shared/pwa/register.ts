import { readonly, ref } from 'vue'

import { api } from '@/shared/api/http'

/** How often an open tab asks the static host for a new service worker, at most. */
export const updateCheckInterval = 60 * 60 * 1000

const waiting = ref(false)
let updateSW: ((reloadPage?: boolean) => Promise<void>) | null = null

/** True when a new version is installed and waits for the user to reload. */
export const needRefresh = readonly(waiting)

/** Re-checks for a new version when the tab comes back, throttled to `updateCheckInterval`. */
export function watchForUpdates(registration: ServiceWorkerRegistration, now = () => Date.now()) {
  let lastCheck = now()
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState !== 'visible' || now() - lastCheck < updateCheckInterval) return
    lastCheck = now()
    registration.update().catch(() => {})
  })
}

/**
 * The first service worker takes over a tab that already loaded the session and the board list,
 * so those answers never reached its cache. Reading them once more through the worker lets the
 * app open offline.
 */
export function cacheSessionOnFirstInstall(container: ServiceWorkerContainer) {
  if (container.controller) return
  const warm = () => {
    for (const path of ['/me', '/boards']) api.get(path).catch(() => {})
  }
  container.addEventListener('controllerchange', warm, { once: true })
}

/** Registers the service worker (production builds only; dev has none). */
export async function registerServiceWorker() {
  if (!('serviceWorker' in navigator)) return
  cacheSessionOnFirstInstall(navigator.serviceWorker)
  const { registerSW } = await import('virtual:pwa-register')
  updateSW = registerSW({
    onNeedRefresh: () => (waiting.value = true),
    onRegisteredSW: (_url, registration) => {
      if (registration) watchForUpdates(registration)
    },
  })
}

/** Activates the waiting version and reloads the page. */
export function applyUpdate() {
  waiting.value = false
  void updateSW?.(true)
}

export function dismissUpdate() {
  waiting.value = false
}
