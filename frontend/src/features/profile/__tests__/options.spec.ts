import { describe, expect, it } from 'vitest'

import { avatarColors, timeZones } from '../options'

describe('profile options', () => {
  it('lists UTC first and only once', () => {
    const zones = timeZones()
    expect(zones[0]).toBe('UTC')
    expect(zones.filter((zone) => zone === 'UTC')).toHaveLength(1)
  })

  it('uses #RRGGBB colours the API accepts', () => {
    for (const color of avatarColors) expect(color).toMatch(/^#[0-9A-F]{6}$/)
  })
})
