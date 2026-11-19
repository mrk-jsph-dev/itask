import { describe, expect, it } from 'vitest'

import { defaultRecurrence, describeRecurrence, isValid, toInput } from '../recurrence'

describe('describeRecurrence', () => {
  it.each([
    [{ kind: 'Daily', weekdays: [], dayOfMonth: null }, 'Every day'],
    [{ kind: 'Weekly', weekdays: [3, 1], dayOfMonth: null }, 'Every week on Mon, Wed'],
    [{ kind: 'Weekly', weekdays: [0, 6], dayOfMonth: null }, 'Every week on Sat, Sun'],
    [{ kind: 'Weekly', weekdays: [1, 2, 3, 4, 5], dayOfMonth: null }, 'Every weekday'],
    [
      { kind: 'Weekly', weekdays: [0, 1, 2, 3, 4, 5, 6], dayOfMonth: null },
      'Every day of the week',
    ],
    [{ kind: 'Monthly', weekdays: [], dayOfMonth: 1 }, 'Every month on the 1st'],
    [{ kind: 'Monthly', weekdays: [], dayOfMonth: 22 }, 'Every month on the 22nd'],
    [{ kind: 'Monthly', weekdays: [], dayOfMonth: 31 }, 'Every month on the 31st (or last day)'],
  ] as const)('%o → %s', (rule, text) => {
    expect(describeRecurrence({ ...rule, weekdays: [...rule.weekdays] })).toBe(text)
  })
})

describe('recurrence form', () => {
  it('defaults to the due date weekday and day of month', () => {
    // 2030-03-06 is a Wednesday.
    expect(defaultRecurrence('2030-03-06T12:00:00Z', 'list-1')).toEqual({
      kind: 'Weekly',
      weekdays: [3],
      dayOfMonth: 6,
      targetListId: 'list-1',
    })
    expect(defaultRecurrence(null, 'list-1', new Date(2030, 2, 9)).weekdays).toEqual([6])
  })

  it('sends only the field the kind uses', () => {
    const form = { kind: 'Weekly', weekdays: [5, 1], dayOfMonth: 9, targetListId: 'l' } as const
    expect(toInput({ ...form, weekdays: [...form.weekdays] })).toEqual({
      kind: 'Weekly',
      weekdays: [1, 5],
      dayOfMonth: null,
      targetListId: 'l',
    })
    expect(toInput({ ...form, kind: 'Monthly', weekdays: [5] })).toMatchObject({
      weekdays: [],
      dayOfMonth: 9,
    })
  })

  it('needs a weekday or a day 1 to 31', () => {
    const base = { weekdays: [], dayOfMonth: null, targetListId: 'l' }
    expect(isValid({ ...base, kind: 'Weekly' })).toBe(false)
    expect(isValid({ ...base, kind: 'Weekly', weekdays: [2] })).toBe(true)
    expect(isValid({ ...base, kind: 'Monthly', dayOfMonth: 32 })).toBe(false)
    expect(isValid({ ...base, kind: 'Monthly', dayOfMonth: 15 })).toBe(true)
    expect(isValid({ ...base, kind: 'Daily' })).toBe(true)
  })
})
