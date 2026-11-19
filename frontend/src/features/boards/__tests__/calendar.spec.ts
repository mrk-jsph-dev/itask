import { describe, expect, it } from 'vitest'

import {
  addMonths,
  byDay,
  dayKey,
  monthGrid,
  monthKey,
  moveToDay,
  parseMonth,
  rangeOf,
  startOfWeek,
  weekDays,
} from '../calendar'

describe('calendar', () => {
  it('builds whole Monday-first weeks around a month', () => {
    const days = monthGrid(new Date(2026, 10, 1))

    expect(days).toHaveLength(42)
    expect(dayKey(days[0]!)).toBe('2026-10-26')
    expect(dayKey(days[days.length - 1]!)).toBe('2026-12-06')
    expect(days[0]!.getDay()).toBe(1)
  })

  it('handles months that start on a Monday', () => {
    const days = monthGrid(new Date(2026, 5, 1))

    expect(dayKey(days[0]!)).toBe('2026-06-01')
    expect(dayKey(days[days.length - 1]!)).toBe('2026-07-05')
  })

  it('finds the week and its range', () => {
    const sunday = new Date(2026, 10, 22)
    const week = weekDays(sunday)

    expect(dayKey(startOfWeek(sunday))).toBe('2026-11-16')
    expect(week.map(dayKey)).toEqual([
      '2026-11-16',
      '2026-11-17',
      '2026-11-18',
      '2026-11-19',
      '2026-11-20',
      '2026-11-21',
      '2026-11-22',
    ])
    const range = rangeOf(week)
    expect(dayKey(range.to)).toBe('2026-11-23')
  })

  it('parses and steps months', () => {
    const fallback = new Date(2026, 10, 18)

    expect(monthKey(parseMonth('2027-01'))).toBe('2027-01')
    expect(monthKey(parseMonth(null, fallback))).toBe('2026-11')
    expect(monthKey(parseMonth('nope', fallback))).toBe('2026-11')
    expect(monthKey(addMonths(new Date(2026, 11, 1), 1))).toBe('2027-01')
  })

  it('moves a due date to another day keeping its time', () => {
    const due = new Date(2026, 10, 18, 15, 45).toISOString()

    const moved = new Date(moveToDay(due, '2026-11-25'))

    expect(dayKey(moved)).toBe('2026-11-25')
    expect([moved.getHours(), moved.getMinutes()]).toEqual([15, 45])
  })

  it('groups items by local day and skips undated ones', () => {
    const items = [
      { id: 'a', dueAt: new Date(2026, 10, 18, 9).toISOString() },
      { id: 'b', dueAt: new Date(2026, 10, 18, 22).toISOString() },
      { id: 'c', dueAt: null },
    ]

    const days = byDay(items)

    expect([...days.keys()]).toEqual(['2026-11-18'])
    expect(days.get('2026-11-18')!.map((i) => i.id)).toEqual(['a', 'b'])
  })
})
