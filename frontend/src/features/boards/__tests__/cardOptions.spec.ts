import { describe, expect, it } from 'vitest'

import { dueState, formatDue, fromDateInput, toDateInput } from '../cardOptions'

const now = new Date('2026-11-18T12:00:00')

describe('dueState', () => {
  it('is overdue once the due time has passed', () => {
    expect(dueState(new Date('2026-11-18T11:59:00').toISOString(), null, now)).toBe('overdue')
  })

  it('is soon within two days and later after that', () => {
    expect(dueState(new Date('2026-11-20T11:00:00').toISOString(), null, now)).toBe('soon')
    expect(dueState(new Date('2026-11-21T12:00:00').toISOString(), null, now)).toBe('later')
  })

  it('never flags a completed card', () => {
    expect(dueState('2026-01-01T00:00:00Z', '2026-01-02T00:00:00Z', now)).toBe('done')
  })
})

describe('date helpers', () => {
  it('round-trips a local date through the date input', () => {
    const due = fromDateInput('2026-11-24', 'end')!
    expect(toDateInput(due)).toBe('2026-11-24')
    expect(new Date(due).getHours()).toBe(23)
    expect(new Date(fromDateInput('2026-11-24', 'start')!).getHours()).toBe(0)
  })

  it('treats an empty input as no date', () => {
    expect(fromDateInput('', 'end')).toBeNull()
    expect(toDateInput(null)).toBe('')
  })

  it('shows the year only for other years', () => {
    expect(formatDue(new Date('2026-11-24T10:00:00').toISOString(), now)).not.toMatch(/2026/)
    expect(formatDue(new Date('2027-01-05T10:00:00').toISOString(), now)).toMatch(/2027/)
  })
})
