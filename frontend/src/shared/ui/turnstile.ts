/** True when a Turnstile site key is configured and forms must wait for a token. */
export const turnstileEnabled = Boolean(import.meta.env.VITE_TURNSTILE_SITE_KEY)
