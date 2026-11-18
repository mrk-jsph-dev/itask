import { describe, expect, it } from 'vitest'

import { initials } from '../initials'

describe('initials', () => {
  it('uses the first and last word', () => {
    expect(initials('Ada King Lovelace')).toBe('AL')
  })

  it('uses one letter for a single name', () => {
    expect(initials('  grace ')).toBe('G')
  })

  it('falls back to the email', () => {
    expect(initials('', 'zed@example.com')).toBe('Z')
    expect(initials(null)).toBe('?')
  })
})
