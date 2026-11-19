/** Day and time labels for newest-first feeds (activity, notifications). */

const dayKey = (d: Date) => `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`

/** "Today", "Yesterday", otherwise e.g. "Mon, Nov 16" (with the year when it differs). */
export function dayLabel(iso: string, now = new Date()): string {
  const date = new Date(iso)
  const yesterday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1)
  if (dayKey(date) === dayKey(now)) return 'Today'
  if (dayKey(date) === dayKey(yesterday)) return 'Yesterday'
  return date.toLocaleDateString('en', {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    ...(date.getFullYear() === now.getFullYear() ? {} : { year: 'numeric' }),
  })
}

export function timeLabel(iso: string): string {
  return new Date(iso).toLocaleTimeString('en', { hour: 'numeric', minute: '2-digit' })
}

export interface DayGroup<T> {
  day: string
  items: T[]
}

/** Groups newest-first entries by local day, keeping their order. */
export function groupByDay<T extends { createdAt: string }>(
  entries: readonly T[],
  now = new Date(),
): DayGroup<T>[] {
  const groups: DayGroup<T>[] = []
  for (const entry of entries) {
    const day = dayLabel(entry.createdAt, now)
    const last = groups[groups.length - 1]
    if (last?.day === day) last.items.push(entry)
    else groups.push({ day, items: [entry] })
  }
  return groups
}
