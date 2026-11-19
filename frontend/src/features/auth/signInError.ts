import { ApiError } from '@/shared/api/http'

/**
 * Message for a failed sign-in. ASP.NET Core Identity answers 401 with the sign-in result in
 * `detail` ("Lockedout", "NotAllowed", "Failed").
 */
export function signInErrorMessage(e: unknown): string {
  if (!(e instanceof ApiError)) return 'Something went wrong.'

  if (e.status === 401) {
    switch (e.detail?.toLowerCase()) {
      case 'lockedout':
        return 'Too many failed attempts. This account is locked for a few minutes, so try again later.'
      case 'notallowed':
        return 'Confirm your email first.'
      default:
        return 'Email or password is incorrect.'
    }
  }

  if (e.status === 429) return 'Too many sign-in attempts. Wait a minute and try again.'
  if (e.status === 400) return e.message
  return 'Something went wrong.'
}
