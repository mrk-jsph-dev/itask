import type { Recurrence, RecurrenceInput, RecurrenceKind } from './api'

export const recurrenceKinds: { value: RecurrenceKind; label: string }[] = [
  { value: 'Daily', label: 'Daily' },
  { value: 'Weekly', label: 'Weekly' },
  { value: 'Monthly', label: 'Monthly' },
]

/** Monday first, as the calendar shows weeks; values follow `Date.getDay()`. */
export const weekdayOptions = [
  { value: 1, short: 'Mon', long: 'Monday' },
  { value: 2, short: 'Tue', long: 'Tuesday' },
  { value: 3, short: 'Wed', long: 'Wednesday' },
  { value: 4, short: 'Thu', long: 'Thursday' },
  { value: 5, short: 'Fri', long: 'Friday' },
  { value: 6, short: 'Sat', long: 'Saturday' },
  { value: 0, short: 'Sun', long: 'Sunday' },
] as const

function ordinal(n: number) {
  const rule = new Intl.PluralRules('en', { type: 'ordinal' }).select(n)
  const suffix = { one: 'st', two: 'nd', few: 'rd', other: 'th' }[rule as 'one'] ?? 'th'
  return `${n}${suffix}`
}

/** "Every day", "Every week on Mon, Wed", "Every month on the 31st (or last day)". */
export function describeRecurrence(
  rule: Pick<RecurrenceInput, 'kind' | 'weekdays' | 'dayOfMonth'>,
) {
  switch (rule.kind) {
    case 'Daily':
      return 'Every day'
    case 'Weekly': {
      const days = weekdayOptions.filter((d) => rule.weekdays.includes(d.value))
      if (days.length === 7) return 'Every day of the week'
      if (days.length === 5 && !rule.weekdays.includes(0) && !rule.weekdays.includes(6))
        return 'Every weekday'
      return `Every week on ${days.map((d) => d.short).join(', ')}`
    }
    case 'Monthly': {
      const day = rule.dayOfMonth ?? 1
      return `Every month on the ${ordinal(day)}${day > 28 ? ' (or last day)' : ''}`
    }
  }
}

/**
 * A starting form for a card without a rule: weekly on the due date's weekday (else today's),
 * monthly on its day of month, copies going to the board's first list.
 */
export function defaultRecurrence(
  dueAt: string | null,
  firstListId: string,
  today = new Date(),
): RecurrenceInput {
  const anchor = dueAt ? new Date(dueAt) : today
  return {
    kind: 'Weekly',
    weekdays: [anchor.getDay()],
    dayOfMonth: anchor.getDate(),
    targetListId: firstListId,
  }
}

/** The body to send: only the field the kind uses. */
export function toInput(form: RecurrenceInput): RecurrenceInput {
  return {
    kind: form.kind,
    weekdays: form.kind === 'Weekly' ? [...form.weekdays].sort((a, b) => a - b) : [],
    dayOfMonth: form.kind === 'Monthly' ? form.dayOfMonth : null,
    targetListId: form.targetListId,
  }
}

/** True when the form can be saved. */
export function isValid(form: RecurrenceInput) {
  if (form.kind === 'Weekly') return form.weekdays.length > 0
  if (form.kind === 'Monthly')
    return Number.isInteger(form.dayOfMonth) && form.dayOfMonth! >= 1 && form.dayOfMonth! <= 31
  return true
}

/** "Next: Mon 9 Mar" for the pill's subtitle, in the viewer's locale. */
export function nextRunText(rule: Recurrence) {
  const date = new Date(rule.nextRunAt)
  return `Next: ${date.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short' })}`
}
