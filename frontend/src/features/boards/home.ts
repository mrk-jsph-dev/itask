/** "6 tasks are due this week. 2 are overdue." with singulars and a quiet line for none. */
export function dueSummary(dueThisWeek: number, overdue: number): string {
  if (dueThisWeek === 0) return 'Nothing is due this week.'
  const due =
    dueThisWeek === 1 ? '1 task is due this week.' : `${dueThisWeek} tasks are due this week.`
  if (overdue === 0) return due
  return `${due} ${overdue === 1 ? '1 is' : `${overdue} are`} overdue.`
}

const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime()

/** "Today", "Tomorrow", "Yesterday", else "Nov 20" (local dates). */
export function dueDayLabel(iso: string, now = new Date()): string {
  const days = Math.round((startOfDay(new Date(iso)) - startOfDay(now)) / 86_400_000)
  if (days === 0) return 'Today'
  if (days === 1) return 'Tomorrow'
  if (days === -1) return 'Yesterday'
  return new Date(iso).toLocaleDateString('en', { month: 'short', day: 'numeric' })
}
