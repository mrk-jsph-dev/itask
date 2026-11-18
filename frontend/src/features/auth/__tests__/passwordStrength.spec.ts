import { describe, expect, it } from 'vitest'

import { passwordStrength } from '../passwordStrength'

describe('passwordStrength', () => {
  it('is empty for no password', () => {
    expect(passwordStrength('').score).toBe(0)
  })

  it('caps short passwords at weak', () => {
    const result = passwordStrength('Ab1!')
    expect(result.score).toBe(1)
    expect(result.hint).toMatch(/at least 10/)
  })

  it('counts each rule once the length is met', () => {
    expect(passwordStrength('abcdefghij').score).toBe(1)
    expect(passwordStrength('Abcdefghij').score).toBe(2)
    expect(passwordStrength('Abcdefghi1').score).toBe(3)
  })

  it('suggests a symbol before calling it strong', () => {
    expect(passwordStrength('Abcdefghi1').hint).toBe('Add a symbol to make it strong.')
    const strong = passwordStrength('Abcdefgh1!')
    expect(strong.score).toBe(4)
    expect(strong.label).toBe('Strong')
  })
})
