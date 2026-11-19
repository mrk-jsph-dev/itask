import { beforeEach, describe, expect, it } from 'vitest'

import {
  externalErrorMessage,
  externalUrl,
  isExternalProvider,
  safeNext,
  startExternal,
  takePendingExternal,
} from '../external'

describe('external sign-in', () => {
  beforeEach(() => sessionStorage.clear())

  it('builds the challenge URL with the nonce and an optional link ticket', () => {
    expect(externalUrl('github', 'abc')).toBe('/api/auth/external/github?nonce=abc')
    expect(externalUrl('microsoft', 'abc', 't+/=')).toBe(
      '/api/auth/external/microsoft?nonce=abc&link=t%2B%2F%3D',
    )
  })

  it('keeps a fresh nonce for the tab and hands it back once', () => {
    let url = ''
    startExternal('github', { next: '/boards/1' }, (u) => (url = u))

    const pending = takePendingExternal()
    expect(pending.nonce).toMatch(/^[0-9a-f]{48}$/)
    expect(url).toContain(`nonce=${pending.nonce}`)
    expect(pending.next).toBe('/boards/1')
    expect(takePendingExternal()).toEqual({ nonce: null, next: null })
  })

  it('forgets an older next page when a new sign-in has none', () => {
    startExternal('github', { next: '/old' }, () => {})
    startExternal('github', {}, () => {})

    expect(takePendingExternal().next).toBeNull()
  })

  it('only continues to in-app paths', () => {
    expect(safeNext('/boards/1?view=list')).toBe('/boards/1?view=list')
    expect(safeNext('//evil.example')).toBeNull()
    expect(safeNext('/\\evil.example')).toBeNull()
    expect(safeNext('https://evil.example')).toBeNull()
    expect(safeNext(null)).toBeNull()
  })

  it('explains each callback error', () => {
    expect(externalErrorMessage('account_exists')).toMatch(/Sign in with your password/)
    expect(externalErrorMessage('email_unverified')).toMatch(/verified email/)
    expect(externalErrorMessage('login_taken')).toMatch(/another iTask user/)
    expect(externalErrorMessage('anything')).toMatch(/try again/)
  })

  it('recognises provider ids', () => {
    expect(isExternalProvider('github')).toBe(true)
    expect(isExternalProvider('gitlab')).toBe(false)
  })
})
