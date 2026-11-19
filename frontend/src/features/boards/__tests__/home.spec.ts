import { describe, expect, it } from 'vitest'

import { dueDayLabel, dueSummary } from '../home'

describe('dueSummary', () => {
  it('reads naturally for none, one and many', () => {
    expect(dueSummary(0, 0)).toBe('Nothing is due this week.')
    expect(dueSummary(1, 0)).toBe('1 task is due this week.')
    expect(dueSummary(6, 2)).toBe('6 tasks are due this week. 2 are overdue.')
    expect(dueSummary(3, 1)).toBe('3 tasks are due this week. 1 is overdue.')
  })
})

describe('dueDayLabel', () => {
  const now = new Date(2026, 10, 19, 15, 0)

  it('names nearby days and dates the rest', () => {
    expect(dueDayLabel(new Date(2026, 10, 19, 23, 59).toISOString(), now)).toBe('Today')
    expect(dueDayLabel(new Date(2026, 10, 20, 0, 1).toISOString(), now)).toBe('Tomorrow')
    expect(dueDayLabel(new Date(2026, 10, 18, 9).toISOString(), now)).toBe('Yesterday')
    expect(dueDayLabel(new Date(2026, 10, 24, 12).toISOString(), now)).toBe('Nov 24')
  })
})
