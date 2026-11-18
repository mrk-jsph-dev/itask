/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Empty in development (Vite proxies /api). The API's public URL in production. */
  readonly VITE_API_BASE_URL?: string
}
