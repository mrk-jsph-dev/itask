import { baseUrl } from '@/shared/api/http'

export type ExternalProvider = 'github' | 'microsoft'

export const providerLabels: Record<ExternalProvider, string> = {
  github: 'GitHub',
  microsoft: 'Microsoft',
}

export function isExternalProvider(value: string): value is ExternalProvider {
  return value in providerLabels
}

const nonceKey = 'itask.external.nonce'
const nextKey = 'itask.external.next'

function newNonce(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(24))
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
}

/** API URL that starts the provider's sign-in; `link` is a ticket from the profile's Connect button. */
export function externalUrl(provider: ExternalProvider, nonce: string, link?: string): string {
  const query = new URLSearchParams({ nonce })
  if (link) query.set('link', link)
  return `${baseUrl}/api/auth/external/${provider}?${query.toString()}`
}

/**
 * Leaves for the provider. The nonce stays in this tab's sessionStorage; the API only accepts the code
 * it sends back together with it, so a code opened in another browser is useless.
 */
export function startExternal(
  provider: ExternalProvider,
  options: { next?: string; link?: string } = {},
  navigate: (url: string) => void = (url) => window.location.assign(url),
) {
  const nonce = newNonce()
  try {
    sessionStorage.setItem(nonceKey, nonce)
    if (options.next) sessionStorage.setItem(nextKey, options.next)
    else sessionStorage.removeItem(nextKey)
  } catch {
    // Without sessionStorage the completion page reports an expired sign-in.
  }
  navigate(externalUrl(provider, nonce, options.link))
}

/** Reads and forgets the pending nonce and the page to return to. */
export function takePendingExternal(): { nonce: string | null; next: string | null } {
  try {
    const nonce = sessionStorage.getItem(nonceKey)
    const next = sessionStorage.getItem(nextKey)
    sessionStorage.removeItem(nonceKey)
    sessionStorage.removeItem(nextKey)
    return { nonce, next }
  } catch {
    return { nonce: null, next: null }
  }
}

/** Message for the `error` the API's callback sends back. */
export function externalErrorMessage(code: string | null | undefined): string {
  switch (code) {
    case 'email_unverified':
      return 'That account has no verified email address. Verify one with the provider, or register with a password.'
    case 'account_exists':
      return 'An iTask account already uses this email. Sign in with your password, then connect this provider from your profile.'
    case 'login_taken':
      return 'That account is already connected to another iTask user.'
    default:
      return 'Sign-in did not finish. Please try again.'
  }
}

/** An in-app path to continue to, or null for anything that could leave the site. */
export function safeNext(next: string | null | undefined): string | null {
  return next && next.startsWith('/') && !next.startsWith('//') && !next.startsWith('/\\')
    ? next
    : null
}
