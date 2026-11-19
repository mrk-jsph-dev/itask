/** Local calendar helpers for the board's Calendar view. Weeks start on Monday. */

const pad = (n: number) => String(n).padStart(2, '0')

/** `yyyy-mm-dd` of a local date. */
export function dayKey(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** `yyyy-mm` of a local date. */
export function monthKey(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}`
}

/** First day of a `yyyy-mm` month, or of `fallback`'s month when the key is missing. */
export function parseMonth(key: string | null, fallback = new Date()): Date {
  const match = key ? /^(\d{4})-(\d{2})$/.exec(key) : null
  if (!match) return new Date(fallback.getFullYear(), fallback.getMonth(), 1)
  return new Date(Number(match[1]), Number(match[2]) - 1, 1)
}

export function addMonths(month: Date, delta: number): Date {
  return new Date(month.getFullYear(), month.getMonth() + delta, 1)
}

export function addDays(date: Date, delta: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + delta)
}

/** Monday on or before `date`. */
export function startOfWeek(date: Date): Date {
  return addDays(date, -((date.getDay() + 6) % 7))
}

/** Every day shown for a month: whole weeks from the Monday before the 1st to the Sunday after the last. */
export function monthGrid(month: Date): Date[] {
  const start = startOfWeek(new Date(month.getFullYear(), month.getMonth(), 1))
  const last = new Date(month.getFullYear(), month.getMonth() + 1, 0)
  const end = addDays(startOfWeek(last), 7)
  const days: Date[] = []
  for (let d = start; d < end; d = addDays(d, 1)) days.push(d)
  return days
}

/** The seven days of the week containing `date`. */
export function weekDays(date: Date): Date[] {
  const start = startOfWeek(date)
  return Array.from({ length: 7 }, (_, i) => addDays(start, i))
}

/** [from, to) covering the given days, for the API's `dueFrom`/`dueTo`. */
export function rangeOf(days: Date[]): { from: Date; to: Date } {
  return { from: days[0]!, to: addDays(days[days.length - 1]!, 1) }
}

/** Moves a due date to another local day, keeping its time of day. */
export function moveToDay(dueAt: string, day: string): string {
  const due = new Date(dueAt)
  const [y, m, d] = day.split('-').map(Number)
  const moved = new Date(
    y!,
    m! - 1,
    d!,
    due.getHours(),
    due.getMinutes(),
    due.getSeconds(),
    due.getMilliseconds(),
  )
  return moved.toISOString()
}

/** Groups items with a due date by their local day key. */
export function byDay<T extends { dueAt: string | null }>(items: T[]): Map<string, T[]> {
  const days = new Map<string, T[]>()
  for (const item of items) {
    if (!item.dueAt) continue
    const key = dayKey(new Date(item.dueAt))
    days.set(key, [...(days.get(key) ?? []), item])
  }
  return days
}
