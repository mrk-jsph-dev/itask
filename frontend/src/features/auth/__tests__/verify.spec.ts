import { describe, expect, it } from 'vitest'

import { cleanCode, maskEmail } from '../verify'

describe('maskEmail', () => {
  it('keeps the first letter and the domain', () => {
    expect(maskEmail('ada@example.com')).toBe('a**@example.com')
    expect(maskEmail('a@example.com')).toBe('a*@example.com')
    expect(maskEmail('not-an-email')).toBe('not-an-email')
  })
})

describe('cleanCode', () => {
  it('keeps up to six digits from a pasted code', () => {
    expect(cleanCode(' 123-456 ')).toBe('123456')
    expect(cleanCode('12 34 56 78')).toBe('123456')
    expect(cleanCode('abc')).toBe('')
  })
})
