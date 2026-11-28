import type { RouteLocationRaw } from 'vue-router'

import type { AppNotification, DigestTypes, NotificationType } from './api'

/** One row: `{actor} {what} {target}{suffix}`, e.g. "Ada · mentioned you on · Fix login". */
export interface NotificationLine {
  emoji: string
  /** Bold lead; null for reminders, which start with the card. */
  actor: string | null
  what: string
  target: string | null
  suffix: string | null
  /** Where the row leads; null when it has its own action (invites). */
  to: RouteLocationRaw | null
}

const str = (value: unknown): string | null => (typeof value === 'string' ? value : null)

function cardTitle(data: Record<string, unknown>): string | null {
  const card = data.card as { title?: unknown } | undefined
  return str(card?.title)
}

/** "Thu 3:00 PM" in the viewer's time zone. */
export function dueLabel(iso: string): string {
  return new Date(iso).toLocaleString('en', {
    weekday: 'short',
    hour: 'numeric',
    minute: '2-digit',
  })
}

/** Builds a row's sentence; keep in step with the digest's `DigestText` on the server. */
export function describeNotification(n: AppNotification): NotificationLine {
  const actor = n.actor?.displayName ?? 'Someone'
  const card = cardTitle(n.data) ?? 'a card'
  const to: RouteLocationRaw | null = n.cardId
    ? { name: 'card', params: { boardId: n.board.id, cardId: n.cardId } }
    : null
  const due = str(n.data.dueAt)

  switch (n.type) {
    case 'mentioned':
      return { emoji: '💬', actor, what: 'mentioned you on', target: card, suffix: null, to }
    case 'comment_on_my_card':
      return { emoji: '💬', actor, what: 'commented on', target: card, suffix: null, to }
    case 'assigned':
      return { emoji: '🙋', actor, what: 'assigned you to', target: card, suffix: null, to }
    case 'due_soon':
      return {
        emoji: '⏰',
        actor: null,
        what: 'Due soon:',
        target: card,
        suffix: due ? ` · ${dueLabel(due)}` : null,
        to,
      }
    case 'unblocked':
      return { emoji: '🔓', actor: null, what: 'Unblocked:', target: card, suffix: null, to }
    case 'overdue':
      return { emoji: '🔥', actor: null, what: 'Overdue:', target: card, suffix: null, to }
    case 'invited':
      return {
        emoji: '👋',
        actor,
        what: 'invited you to',
        target: n.board.name,
        suffix: null,
        to: null,
      }
    case 'automation': {
      const text = str(n.data.text)
      return {
        emoji: '⚡',
        actor: str(n.data.rule) ?? 'An automation',
        what: 'flagged',
        target: card,
        suffix: text ? ` · ${text}` : null,
        to,
      }
    }
    default:
      return { emoji: '🔔', actor: null, what: 'Update on', target: n.board.name, suffix: null, to }
  }
}

/** The invite id an `invited` notification can be accepted with. */
export function inviteIdOf(n: AppNotification): string | null {
  return n.type === 'invited' ? str(n.data.inviteId) : null
}

/** Badge text: hidden at 0, capped at "9+". */
export function badgeText(count: number): string | null {
  if (count <= 0) return null
  return count > 9 ? '9+' : String(count)
}

/** Digest type toggles, labelled for the profile form. */
export const digestTypeOptions: readonly {
  key: keyof DigestTypes
  label: string
  type: NotificationType
}[] = [
  { key: 'mentioned', label: 'Mentions', type: 'mentioned' },
  { key: 'assigned', label: 'Assigned to me', type: 'assigned' },
  { key: 'commentOnMyCard', label: 'Comments on my cards', type: 'comment_on_my_card' },
  { key: 'dueSoon', label: 'Due soon', type: 'due_soon' },
  { key: 'overdue', label: 'Overdue', type: 'overdue' },
  { key: 'invited', label: 'Board invites', type: 'invited' },
  { key: 'unblocked', label: 'My cards unblocked', type: 'unblocked' },
]
