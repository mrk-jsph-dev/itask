import { describe, expect, it } from 'vitest'

import { greeting, inviteLink } from '../options'

describe('greeting', () => {
  it.each([
    [8, 'Good morning'],
    [13, 'Good afternoon'],
    [20, 'Good evening'],
  ])('at %i:00 says %s', (hour, expected) => {
    expect(greeting(new Date(2026, 10, 18, hour))).toBe(expected)
  })
})

describe('inviteLink', () => {
  it('builds an absolute, encoded link', () => {
    expect(inviteLink('a+b', 'https://itask.example')).toBe('https://itask.example/invite/a%2Bb')
  })
})
