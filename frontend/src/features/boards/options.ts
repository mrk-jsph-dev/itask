export { colorPalette as boardColors } from '@/shared/ui/palette'

/** Emoji offered in the New board sheet and board settings. */
export const boardEmojis = [
  '📋',
  '🚀',
  '🎯',
  '💡',
  '🛠',
  '📈',
  '🎨',
  '📚',
  '🏠',
  '✈️',
  '🛒',
  '💼',
  '🧪',
  '🎉',
  '🌱',
  '🔒',
] as const

/** "Good morning" before noon, "Good afternoon" until 6 pm, then "Good evening". */
export function greeting(now: Date = new Date()): string {
  const hour = now.getHours()
  if (hour < 12) return 'Good morning'
  if (hour < 18) return 'Good afternoon'
  return 'Good evening'
}

/** Absolute link for an invite token, shown once after the invite is created. */
export function inviteLink(token: string, origin: string = window.location.origin): string {
  return `${origin}/invite/${encodeURIComponent(token)}`
}

export function byName<T extends { name: string }>(a: T, b: T): number {
  return a.name.localeCompare(b.name, undefined, { sensitivity: 'base' })
}
