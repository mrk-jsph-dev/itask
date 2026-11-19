/// <reference types="vite/client" />
/// <reference types="vite-plugin-pwa/client" />

interface ImportMetaEnv {
  /** Empty in development (Vite proxies /api). The API's public URL in production. */
  readonly VITE_API_BASE_URL?: string
  /** Cloudflare Turnstile site key. Empty disables the widget (the API then skips the check too). */
  readonly VITE_TURNSTILE_SITE_KEY?: string
}
