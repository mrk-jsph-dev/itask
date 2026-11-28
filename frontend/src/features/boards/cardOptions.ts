import type { CardPriority } from './api'

export const priorities: readonly CardPriority[] = ['None', 'Low', 'Medium', 'High', 'Urgent']

const pill = (name: string) => ({
  color: `var(--color-prio-${name})`,
  bg: `var(--color-prio-${name}-bg)`,
})

/** Pill colours (theme tokens) from DESIGN.md; Urgent is a filled red. None shows no pill. */
export const priorityStyle: Record<Exclude<CardPriority, 'None'>, { color: string; bg: string }> = {
  Low: pill('low'),
  Medium: pill('medium'),
  High: pill('high'),
  Urgent: pill('urgent'),
}

/** Emoji offered for labels. */
export const labelEmojis = ['🖥️', '⚙️', '☁️', '🐞', '✨', '📣', '🔒', '📈', '🎨', '🧪'] as const

export type DueState = 'done' | 'overdue' | 'soon' | 'later'

const day = 24 * 60 * 60 * 1000

/** How a due date reads: overdue once past, soon within 48 h; completed cards are never overdue. */
export function dueState(dueAt: string, completedAt: string | null, now = new Date()): DueState {
  if (completedAt) return 'done'
  const left = new Date(dueAt).getTime() - now.getTime()
  if (left < 0) return 'overdue'
  return left <= 2 * day ? 'soon' : 'later'
}

/** "Nov 24", with the year only when it isn't the current one. */
export function formatDue(iso: string, now = new Date()): string {
  const date = new Date(iso)
  return date.toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    ...(date.getFullYear() === now.getFullYear() ? {} : { year: 'numeric' }),
  })
}

/** `yyyy-mm-dd` in local time for a date input, or '' for none. */
export function toDateInput(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** A date input's value as the end of that local day (due) or its start (start), in ISO. */
export function fromDateInput(value: string, edge: 'start' | 'end'): string | null {
  if (!value) return null
  const [y, m, d] = value.split('-').map(Number)
  const date =
    edge === 'end' ? new Date(y!, m! - 1, d!, 23, 59, 59) : new Date(y!, m! - 1, d!, 0, 0, 0)
  return date.toISOString()
}

/** "just now", "5 minutes ago", "2 hours ago", "3 days ago", then a short date. */
export function formatRelative(iso: string, now = new Date()): string {
  const seconds = Math.round((now.getTime() - new Date(iso).getTime()) / 1000)
  if (seconds < 45) return 'just now'
  const rtf = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return rtf.format(-minutes, 'minute')
  const hours = Math.round(minutes / 60)
  if (hours < 24) return rtf.format(-hours, 'hour')
  const days = Math.round(hours / 24)
  if (days < 7) return rtf.format(-days, 'day')
  return new Date(iso).toLocaleDateString('en', { month: 'short', day: 'numeric' })
}
