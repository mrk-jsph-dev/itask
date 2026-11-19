/** Runtime cache the service worker keeps API reads in (see vite.config.ts). */
export const apiCacheName = 'itask-api'

/** Drops cached API reads, so the next person on this device can't read them offline. */
export async function clearApiCache() {
  if (typeof caches === 'undefined') return
  try {
    await caches.delete(apiCacheName)
  } catch {
    // Storage can be unavailable (private mode); nothing was cached then.
  }
}
