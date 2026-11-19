export interface TurnstileApi {
  render(el: HTMLElement, options: Record<string, unknown>): string
  reset(id: string): void
  remove(id: string): void
}

declare global {
  interface Window {
    turnstile?: TurnstileApi
  }
}

/** True when a Turnstile site key is configured and forms must wait for a token. */
export const turnstileEnabled = Boolean(import.meta.env.VITE_TURNSTILE_SITE_KEY)

const scriptUrl = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit'
let loading: Promise<void> | undefined

/** Adds Cloudflare's script once per page load, however many widgets mount and unmount. */
export function loadTurnstile(): Promise<void> {
  loading ??= new Promise((resolve, reject) => {
    if (window.turnstile) return resolve()
    const script = document.createElement('script')
    script.src = scriptUrl
    script.async = true
    script.onload = () => resolve()
    script.onerror = () => {
      loading = undefined
      reject(new Error('Turnstile failed to load'))
    }
    document.head.appendChild(script)
  })
  return loading
}
