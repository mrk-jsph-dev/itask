/** Pure helpers for the board's Timeline view. Weeks start on Monday; all days are local. */
import type { Card, CardPatch } from './api'
import { addDays, dayKey, startOfWeek } from './calendar'
import type { TimelineZoom } from './filters'

export interface TimelineWindow {
  /** First shown day, local midnight. */
  from: Date
  /** Day after the last shown day, local midnight. */
  to: Date
  days: Date[]
}

/** Days each zoom shows (month uses the calendar month). */
const spanDays: Record<Exclude<TimelineZoom, 'month'>, number> = { week: 14, quarter: 91 }

const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate())

/** Local midnight of a `yyyy-mm-dd` key, or null when it doesn't parse. */
export function parseDay(key: string | null): Date | null {
  const match = key ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(key) : null
  return match ? new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3])) : null
}

/** The first day of the window that holds `anchor` at a zoom. */
export function windowStart(anchor: Date, zoom: TimelineZoom): Date {
  return zoom === 'month'
    ? new Date(anchor.getFullYear(), anchor.getMonth(), 1)
    : startOfWeek(startOfDay(anchor))
}

/** The days a zoom shows from `fromKey` (or from the window holding `now`). */
export function windowFor(
  fromKey: string | null,
  zoom: TimelineZoom,
  now = new Date(),
): TimelineWindow {
  const from = windowStart(parseDay(fromKey) ?? now, zoom)
  const to =
    zoom === 'month'
      ? new Date(from.getFullYear(), from.getMonth() + 1, 1)
      : addDays(from, spanDays[zoom])
  const days: Date[] = []
  for (let d = from; d < to; d = addDays(d, 1)) days.push(d)
  return { from, to, days }
}

/** The next or previous window start: a week, a month, or four weeks. */
export function stepWindow(from: Date, zoom: TimelineZoom, delta: -1 | 1): Date {
  if (zoom === 'month') return new Date(from.getFullYear(), from.getMonth() + delta, 1)
  return addDays(from, delta * (zoom === 'week' ? 7 : 28))
}

/** The `from` URL value for a window start: null when it is the default (holds today). */
export function fromKeyFor(from: Date, zoom: TimelineZoom, now = new Date()): string | null {
  return dayKey(from) === dayKey(windowStart(now, zoom)) ? null : dayKey(from)
}

/** Whole local days from `from` to the day holding `date` (negative before it). */
export function dayIndex(date: Date, from: Date): number {
  return Math.round((startOfDay(date).getTime() - from.getTime()) / 86_400_000)
}

export interface BarGeometry {
  /** `bar` has a start and a due date; `dot` has only one of them. */
  kind: 'bar' | 'dot'
  /** Left edge (bar) or centre (dot), percent of the track. */
  left: number
  /** Width in percent of the track (0 for a dot). */
  width: number
  /** The bar starts before / ends after the window. */
  clippedStart: boolean
  clippedEnd: boolean
  /** Day indexes covered, end exclusive; may fall outside 0..days. */
  startDay: number
  endDay: number
}

/** Where a dated card sits on the track; null when it has no dates. */
export function barGeometry(
  card: Pick<Card, 'startAt' | 'dueAt'>,
  window: Pick<TimelineWindow, 'from' | 'days'>,
): BarGeometry | null {
  const startIso = card.startAt ?? card.dueAt
  const endIso = card.dueAt ?? card.startAt
  if (!startIso || !endIso) return null
  const n = window.days.length
  const startDay = dayIndex(new Date(startIso), window.from)
  const endDay = dayIndex(new Date(endIso), window.from) + 1
  const pct = (i: number) => (Math.min(Math.max(i, 0), n) / n) * 100
  if (!card.startAt || !card.dueAt) {
    return {
      kind: 'dot',
      left: ((startDay + 0.5) / n) * 100,
      width: 0,
      clippedStart: startDay < 0,
      clippedEnd: startDay >= n,
      startDay,
      endDay,
    }
  }
  const left = pct(startDay)
  return {
    kind: 'bar',
    left,
    width: pct(endDay) - left,
    clippedStart: startDay < 0,
    clippedEnd: endDay > n,
    startDay,
    endDay,
  }
}

/** `iso` moved by whole local days, keeping its time of day. */
export function shiftIso(iso: string, days: number): string {
  const d = new Date(iso)
  return new Date(
    d.getFullYear(),
    d.getMonth(),
    d.getDate() + days,
    d.getHours(),
    d.getMinutes(),
    d.getSeconds(),
    d.getMilliseconds(),
  ).toISOString()
}

/** Moves both dates by `days`; null when there is nothing to move. */
export function shiftDates(card: Pick<Card, 'startAt' | 'dueAt'>, days: number): CardPatch | null {
  if (!days || (!card.startAt && !card.dueAt)) return null
  const patch: CardPatch = {}
  if (card.startAt) patch.startAt = shiftIso(card.startAt, days)
  if (card.dueAt) patch.dueAt = shiftIso(card.dueAt, days)
  return patch
}

const endOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate(), 23, 59, 59)

/**
 * Moves one end of a card by `days`, keeping time of day and start ≤ due. A missing start is
 * taken as the due day's midnight; a missing due as the end of the start's day. Null when
 * nothing changes.
 */
export function resizeDates(
  card: Pick<Card, 'startAt' | 'dueAt'>,
  edge: 'start' | 'end',
  days: number,
): CardPatch | null {
  if (!days || (!card.startAt && !card.dueAt)) return null
  const start = card.startAt ?? startOfDay(new Date(card.dueAt!)).toISOString()
  const due = card.dueAt ?? endOfDay(new Date(card.startAt!)).toISOString()
  if (edge === 'start') {
    let next = shiftIso(start, days)
    if (new Date(next) > new Date(due)) next = startOfDay(new Date(due)).toISOString()
    return next === card.startAt ? null : { startAt: next, ...(card.dueAt ? {} : { dueAt: due }) }
  }
  let next = shiftIso(due, days)
  if (new Date(next) < new Date(start)) next = endOfDay(new Date(start)).toISOString()
  return next === card.dueAt ? null : { dueAt: next, ...(card.startAt ? {} : { startAt: start }) }
}

/** Day delta for a horizontal drag of `dx` pixels on a track `width` pixels wide. */
export function dragDays(dx: number, width: number, days: number): number {
  if (width <= 0) return 0
  return Math.round((dx / width) * days) || 0
}

/** The card with a patch applied, for a drag preview. */
export function withPatch<T extends Pick<Card, 'startAt' | 'dueAt'>>(
  card: T,
  patch: CardPatch | null,
): T {
  if (!patch) return card
  return {
    ...card,
    ...('startAt' in patch ? { startAt: patch.startAt ?? null } : {}),
    ...('dueAt' in patch ? { dueAt: patch.dueAt ?? null } : {}),
  }
}

/** "Mar 3 – Mar 9", "Due Mar 9" or "Starts Mar 3". */
export function rangeText(card: Pick<Card, 'startAt' | 'dueAt'>): string {
  const fmt = (iso: string) =>
    new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
  if (card.startAt && card.dueAt) return `${fmt(card.startAt)} – ${fmt(card.dueAt)}`
  if (card.dueAt) return `Due ${fmt(card.dueAt)}`
  if (card.startAt) return `Starts ${fmt(card.startAt)}`
  return 'No dates'
}
