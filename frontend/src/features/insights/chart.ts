import { ranges, type InsightRange, type Insights } from './api'

/** Range from the `days` query value; anything else falls back to 30. */
export function parseRange(value: unknown): InsightRange {
  const n = Number(Array.isArray(value) ? value[0] : value)
  return (ranges as readonly number[]).includes(n) ? (n as InsightRange) : 30
}

export interface StatTile {
  label: string
  value: string
  hint: string
  /** Over a limit: shown with a warning icon and label, never colour alone. */
  warn?: boolean
}

/** Days as a short label: "–" when unknown, "<0.1 d" for very short stays. */
export function formatDays(days: number | null): string {
  if (days === null) return '–'
  if (days > 0 && days < 0.1) return '<0.1 d'
  return `${days.toLocaleString('en-US', { maximumFractionDigits: 1 })} d`
}

/** Open cards past their list's WIP limit, summed over lists. */
export function overWip(insights: Pick<Insights, 'wip'>): number {
  return insights.wip.reduce(
    (sum, l) => sum + (l.limit === null ? 0 : Math.max(0, l.open - l.limit)),
    0,
  )
}

export function tiles(insights: Insights): StatTile[] {
  const done = insights.throughput.reduce((s, d) => s + d.count, 0)
  const over = overWip(insights)
  const lists = insights.wip.filter((l) => l.limit !== null && l.open > l.limit).length
  return [
    { label: 'Done', value: String(done), hint: `in the last ${insights.days} days` },
    {
      label: 'Median cycle time',
      value: formatDays(insights.cycleTime.medianDays),
      hint: 'created to done',
    },
    {
      label: '85th percentile',
      value: formatDays(insights.cycleTime.p85Days),
      hint: '85% of cards finish within',
    },
    {
      label: 'Over WIP',
      value: String(over),
      hint: over
        ? `${lists} ${lists === 1 ? 'list' : 'lists'} over the limit`
        : 'all lists within limits',
      warn: over > 0,
    },
  ]
}

/** True when nothing happened in the range: no cards created, done or moved through lists. */
export function isEmpty(insights: Insights): boolean {
  const total = (days: { count: number }[]) => days.reduce((s, d) => s + d.count, 0)
  return (
    total(insights.throughput) === 0 &&
    total(insights.created) === 0 &&
    insights.timeInList.every((l) => l.samples === 0) &&
    insights.aging.length === 0
  )
}

export interface DayBar {
  date: string
  done: number
  created: number
  /** Short axis label, e.g. "Nov 3". */
  label: string
}

const dayFormat = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  day: 'numeric',
  timeZone: 'UTC',
})

/** `yyyy-MM-dd` as "Nov 3" (the date is already local; formatted as UTC to keep the day). */
export const dayLabel = (date: string) => dayFormat.format(new Date(`${date}T00:00:00Z`))

/** Done and created counts paired per day. */
export function dayBars(insights: Insights): DayBar[] {
  const created = new Map(insights.created.map((d) => [d.date, d.count]))
  return insights.throughput.map((d) => ({
    date: d.date,
    done: d.count,
    created: created.get(d.date) ?? 0,
    label: dayLabel(d.date),
  }))
}

export interface BarGroup {
  /** First and last day in the group. */
  from: string
  to: string
  done: number
  created: number
  /** Axis label: the first day. */
  label: string
  /** Tooltip heading: one day, or "Nov 3 – Nov 9". */
  title: string
}

/** Days per bar group: daily for 30 days, weekly beyond so bars stay readable on a phone. */
export const groupSize = (days: number) => (days > 30 ? 7 : 1)

/**
 * Sums day bars into groups of `size` counted back from the last day, so the latest group always
 * ends today; the oldest group may be shorter.
 */
export function groupBars(days: DayBar[], size: number): BarGroup[] {
  const groups: BarGroup[] = []
  for (let end = days.length; end > 0; end -= size) {
    const slice = days.slice(Math.max(0, end - size), end)
    const first = slice[0]!
    const last = slice[slice.length - 1]!
    groups.unshift({
      from: first.date,
      to: last.date,
      done: slice.reduce((s, d) => s + d.done, 0),
      created: slice.reduce((s, d) => s + d.created, 0),
      label: first.label,
      title: first.date === last.date ? first.label : `${first.label} – ${last.label}`,
    })
  }
  return groups
}

/**
 * A "nice" axis maximum and its ticks for counts: steps of 1, 2, 5 × 10ⁿ (at least 1), about 4 ticks,
 * never below 1 so empty charts still draw a baseline and one gridline.
 */
export function niceScale(max: number, target = 4): { max: number; ticks: number[] } {
  const raw = Math.max(1, max) / target
  const magnitude = 10 ** Math.floor(Math.log10(raw))
  // Counts are whole numbers, so a step is never below 1.
  const step = Math.max(1, [1, 2, 5, 10].map((m) => m * magnitude).find((s) => s >= raw) ?? raw)
  const top = Math.max(step, Math.ceil(Math.max(1, max) / step) * step)
  const ticks: number[] = []
  for (let t = 0; t <= top + step / 2; t += step) ticks.push(Math.round(t * 1000) / 1000)
  return { max: top, ticks }
}

/** Which day labels to print on the x axis: first, last and evenly spaced ones between. */
export function labelIndexes(count: number, wanted = 5): number[] {
  if (count <= 0) return []
  if (count <= wanted) return Array.from({ length: count }, (_, i) => i)
  const step = (count - 1) / (wanted - 1)
  return Array.from({ length: wanted }, (_, i) => Math.round(i * step))
}

export interface ListBar {
  listId: string
  name: string
  emoji: string | null
  days: number | null
  samples: number
  /** 0 to 1 of the widest bar. */
  share: number
}

export function listBars(insights: Insights): ListBar[] {
  const max = Math.max(0, ...insights.timeInList.map((l) => l.medianDays ?? 0))
  return insights.timeInList.map((l) => ({
    listId: l.listId,
    name: l.name,
    emoji: l.emoji,
    days: l.medianDays,
    samples: l.samples,
    share: max > 0 && l.medianDays !== null ? l.medianDays / max : 0,
  }))
}
