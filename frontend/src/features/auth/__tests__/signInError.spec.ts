import { describe, expect, it } from 'vitest'

import { ApiError } from '@/shared/api/http'
import { signInErrorMessage } from '../signInError'

describe('signInErrorMessage', () => {
  it.each([
    ['Lockedout', /locked for a few minutes/],
    ['LockedOut', /locked for a few minutes/],
    ['NotAllowed', /Confirm your email/],
    ['Failed', /incorrect/],
    [undefined, /incorrect/],
  ])('maps 401 detail %s', (detail, expected) => {
    expect(signInErrorMessage(new ApiError(401, 'Unauthorized', undefined, detail))).toMatch(expected)
  })

  it('explains rate limiting', () => {
    expect(signInErrorMessage(new ApiError(429, 'Too Many Requests'))).toMatch(/Wait a minute/)
  })

  it('shows validation messages', () => {
    expect(signInErrorMessage(new ApiError(400, 'Email is required.'))).toBe('Email is required.')
  })

  it('falls back for other errors', () => {
    expect(signInErrorMessage(new ApiError(500, 'Server error'))).toBe('Something went wrong.')
    expect(signInErrorMessage(new TypeError('fetch failed'))).toBe('Something went wrong.')
  })
})
