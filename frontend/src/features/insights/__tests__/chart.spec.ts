import { describe, expect, it } from 'vitest'

import type { Insights } from '../api'
import {
  dayBars,
  formatDays,
  groupBars,
  groupSize,
  isEmpty,
  labelIndexes,
  listBars,
  niceScale,
  overWip,
  parseRange,
  tiles,
} from '../chart'

const day = (n: number) => `2026-11-${String(n).padStart(2, '0')}`

function insights(overrides: Partial<Insights> = {}): Insights {
  return {
    days: 30,
    from: day(1),
    to: day(10),
    throughput: Array.from({ length: 10 }, (_, i) => ({
      date: day(i + 1),
      count: i === 9 ? 2 : 0,
    })),
    created: Array.from({ length: 10 }, (_, i) => ({ date: day(i + 1), count: i === 0 ? 3 : 0 })),
    cycleTime: { count: 2, medianDays: 7.5, p85Days: 25.4 },
    timeInList: [
      { listId: 'l1', name: 'To do', emoji: '📝', medianDays: 4, samples: 3 },
      { listId: 'l2', name: 'Doing', emoji: null, medianDays: 2, samples: 1 },
      { listId: 'l3', name: 'Done', emoji: '✅', medianDays: null, samples: 0 },
    ],
    aging: [{ id: 'c1', key: 'RM-1', title: 'Old', listId: 'l1', listName: 'To do', days: 3 }],
    wip: [
      { listId: 'l1', name: 'To do', emoji: null, open: 5, limit: 3 },
      { listId: 'l2', name: 'Doing', emoji: null, open: 2, limit: 1 },
      { listId: 'l3', name: 'Done', emoji: null, open: 9, limit: null },
    ],
    ...overrides,
  }
}

describe('insights chart mapping', () => {
  it('parses the range from the query, defaulting to 30', () => {
    expect(parseRange('60')).toBe(60)
    expect(parseRange(['90'])).toBe(90)
    expect(parseRange('45')).toBe(30)
    expect(parseRange(undefined)).toBe(30)
  })

  it('formats days', () => {
    expect(formatDays(null)).toBe('–')
    expect(formatDays(0.04)).toBe('<0.1 d')
    expect(formatDays(7.5)).toBe('7.5 d')
    expect(formatDays(12)).toBe('12 d')
  })

  it('sums cards over WIP across limited lists only', () => {
    expect(overWip(insights())).toBe(3)
  })

  it('builds the four stat tiles', () => {
    const [done, median, p85, wip] = tiles(insights())

    expect(done).toMatchObject({ label: 'Done', value: '2', hint: 'in the last 30 days' })
    expect(median?.value).toBe('7.5 d')
    expect(p85?.value).toBe('25.4 d')
    expect(wip).toMatchObject({ value: '3', warn: true, hint: '2 lists over the limit' })
  })

  it('is empty only when nothing was created, done, moved or waiting', () => {
    expect(isEmpty(insights())).toBe(false)
    const zero = (d: { date: string }) => ({ date: d.date, count: 0 })
    const base = insights()
    expect(
      isEmpty({
        ...base,
        throughput: base.throughput.map(zero),
        created: base.created.map(zero),
        timeInList: base.timeInList.map((l) => ({ ...l, samples: 0 })),
        aging: [],
      }),
    ).toBe(true)
  })

  it('pairs done and created per day', () => {
    const bars = dayBars(insights())

    expect(bars).toHaveLength(10)
    expect(bars[0]).toEqual({ date: day(1), done: 0, created: 3, label: 'Nov 1' })
    expect(bars[9]).toMatchObject({ done: 2, created: 0, label: 'Nov 10' })
  })

  it('groups weeks back from the last day', () => {
    const groups = groupBars(dayBars(insights()), 7)

    expect(groups.map((g) => [g.from, g.to])).toEqual([
      [day(1), day(3)],
      [day(4), day(10)],
    ])
    expect(groups[0]).toMatchObject({ created: 3, title: 'Nov 1 – Nov 3' })
    expect(groups[1]).toMatchObject({ done: 2 })
    expect(groupSize(30)).toBe(1)
    expect(groupSize(90)).toBe(7)
  })

  it('picks nice axis ticks', () => {
    expect(niceScale(0)).toEqual({ max: 1, ticks: [0, 1] })
    expect(niceScale(7)).toEqual({ max: 8, ticks: [0, 2, 4, 6, 8] })
    expect(niceScale(23)).toEqual({ max: 30, ticks: [0, 10, 20, 30] })
  })

  it('labels first, last and spaced days', () => {
    expect(labelIndexes(3)).toEqual([0, 1, 2])
    expect(labelIndexes(30)).toEqual([0, 7, 15, 22, 29])
  })

  it('scales list bars to the longest median', () => {
    expect(listBars(insights()).map((b) => b.share)).toEqual([1, 0.5, 0])
  })
})
