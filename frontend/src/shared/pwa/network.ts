import { computed, ref } from 'vue'

/** Tooltip and error text for anything that would change data while offline. */
export const OFFLINE_HINT = "You're offline. Changes are disabled."

/** Header the service worker adds to API answers it served from its cache. */
export const offlineHeader = 'X-ITask-Offline'

const browserOnline = ref(typeof navigator === 'undefined' ? true : navigator.onLine)
const servedFromCache = ref(false)

if (typeof window !== 'undefined') {
  window.addEventListener('online', () => (browserOnline.value = true))
  window.addEventListener('offline', () => (browserOnline.value = false))
}

/**
 * Whether the app can reach the API. Offline it is read-only. `navigator.onLine` alone misses
 * networks without internet, so a cached answer from the service worker also counts as offline,
 * until the next answer that came over the network.
 */
export const online = computed(() => browserOnline.value && !servedFromCache.value)

/** Called by the HTTP client for every API response. */
export function noteResponse(response: Response) {
  servedFromCache.value = response.headers.get(offlineHeader) === '1'
}
