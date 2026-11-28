import { describe, expect, it } from 'vitest'

import { dayKey } from '../calendar'
import {
  barGeometry,
  dragDays,
  fromKeyFor,
  rangeText,
  resizeDates,
  shiftDates,
  stepWindow,
  windowFor,
} from '../timeline'

const at = (m: number, d: number, h = 0, min = 0) => new Date(2026, m - 1, d, h, min).toISOString()
const local = (iso: string | null | undefined) => {
  const d = new Date(iso!)
  return `${dayKey(d)} ${d.getHours()}:${String(d.getMinutes()).padStart(2, '0')}`
}

describe('timeline window', () => {
  const now = new Date(2026, 2, 11) // Wed Mar 11

  it('covers two weeks, a calendar month or 13 weeks', () => {
    const week = windowFor(null, 'week', now)
    const month = windowFor(null, 'month', now)
    const quarter = windowFor(null, 'quarter', now)

    expect([dayKey(week.from), dayKey(week.to), week.days.length]).toEqual([
      '2026-03-09',
      '2026-03-23',
      14,
    ])
    expect([dayKey(month.from), dayKey(month.to), month.days.length]).toEqual([
      '2026-03-01',
      '2026-04-01',
      31,
    ])
    expect([dayKey(quarter.from), quarter.days.length]).toEqual(['2026-03-09', 91])
    expect(dayKey(windowFor('2026-05-20', 'month', now).from)).toBe('2026-05-01')
    expect(dayKey(windowFor('2026-05-20', 'week', now).from)).toBe('2026-05-18')
  })

  it('steps by a week, a month or four weeks and keeps the URL plain on today', () => {
    const from = new Date(2026, 2, 9)

    expect(dayKey(stepWindow(from, 'week', 1))).toBe('2026-03-16')
    expect(dayKey(stepWindow(new Date(2026, 2, 1), 'month', -1))).toBe('2026-02-01')
    expect(dayKey(stepWindow(from, 'quarter', 1))).toBe('2026-04-06')
    expect(fromKeyFor(new Date(2026, 2, 9), 'week', now)).toBeNull()
    expect(fromKeyFor(new Date(2026, 2, 16), 'week', now)).toBe('2026-03-16')
  })
})

describe('bar geometry', () => {
  const win = windowFor('2026-03-01', 'month') // 31 days

  it('places a bar over whole days', () => {
    const g = barGeometry({ startAt: at(3, 2, 9), dueAt: at(3, 4, 17) }, win)!

    expect(g.kind).toBe('bar')
    expect(g.left).toBeCloseTo((1 / 31) * 100)
    expect(g.width).toBeCloseTo((3 / 31) * 100)
    expect([g.clippedStart, g.clippedEnd]).toEqual([false, false])
  })

  it('clips bars that run past the window', () => {
    const g = barGeometry({ startAt: at(2, 20), dueAt: at(4, 10) }, win)!

    expect([g.left, g.width]).toEqual([0, 100])
    expect([g.clippedStart, g.clippedEnd]).toEqual([true, true])
  })

  it('shows a single date as a dot in the middle of its day and nothing without dates', () => {
    const due = barGeometry({ startAt: null, dueAt: at(3, 10, 18) }, win)!
    const start = barGeometry({ startAt: at(3, 1), dueAt: null }, win)!

    expect(due.kind).toBe('dot')
    expect(due.left).toBeCloseTo((9.5 / 31) * 100)
    expect(start.left).toBeCloseTo((0.5 / 31) * 100)
    expect(barGeometry({ startAt: null, dueAt: null }, win)).toBeNull()
  })

  it('scales with the zoom', () => {
    const card = { startAt: at(3, 9), dueAt: at(3, 15) }

    expect(barGeometry(card, windowFor('2026-03-09', 'week'))!.width).toBeCloseTo(50)
    expect(barGeometry(card, windowFor('2026-03-09', 'quarter'))!.width).toBeCloseTo((7 / 91) * 100)
  })
})

describe('date edits', () => {
  it('moves both dates and keeps the time of day', () => {
    const patch = shiftDates({ startAt: at(3, 2, 9, 30), dueAt: at(3, 4, 17) }, 3)!

    expect([local(patch.startAt), local(patch.dueAt)]).toEqual([
      '2026-03-05 9:30',
      '2026-03-07 17:00',
    ])
    expect(shiftDates({ startAt: null, dueAt: at(3, 4) }, 1)).toEqual({ dueAt: at(3, 5) })
    expect(shiftDates({ startAt: at(3, 2), dueAt: at(3, 4) }, 0)).toBeNull()
  })

  it('resizes one end without crossing the other', () => {
    const card = { startAt: at(3, 2, 9), dueAt: at(3, 4, 17) }

    expect(local(resizeDates(card, 'end', 2)!.dueAt)).toBe('2026-03-06 17:00')
    expect(local(resizeDates(card, 'start', -1)!.startAt)).toBe('2026-03-01 9:00')
    expect(local(resizeDates(card, 'start', 5)!.startAt)).toBe('2026-03-04 0:00')
    expect(local(resizeDates(card, 'end', -5)!.dueAt)).toBe('2026-03-02 23:59')
  })

  it('turns a dot into a bar when an end is pulled', () => {
    const patch = resizeDates({ startAt: null, dueAt: at(3, 4, 17) }, 'start', -2)!

    expect([local(patch.startAt), patch.dueAt]).toEqual(['2026-03-02 0:00', undefined])
    const late = resizeDates({ startAt: at(3, 2, 8), dueAt: null }, 'end', 1)!
    expect([local(late.dueAt), late.startAt]).toEqual(['2026-03-03 23:59', undefined])
  })

  it('turns a drag into whole days', () => {
    expect(dragDays(52, 310, 31)).toBe(5)
    expect(dragDays(-2, 310, 31)).toBe(0)
    expect(dragDays(10, 0, 31)).toBe(0)
  })

  it('describes the range', () => {
    expect(rangeText({ startAt: null, dueAt: null })).toBe('No dates')
    expect(rangeText({ startAt: null, dueAt: at(3, 4) })).toMatch(/^Due /)
    expect(rangeText({ startAt: at(3, 4), dueAt: null })).toMatch(/^Starts /)
  })
})
