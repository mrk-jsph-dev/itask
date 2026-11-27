import { api } from '@/shared/api/http'

export type NotificationType =
  | 'mentioned'
  | 'assigned'
  | 'due_soon'
  | 'overdue'
  | 'invited'
  | 'comment_on_my_card'
  | 'unblocked'
  | 'automation'

export interface AppNotification {
  id: string
  type: NotificationType
  /** Names captured when it was created; shape depends on `type`. */
  data: Record<string, unknown>
  createdAt: string
  readAt: string | null
  cardId: string | null
  board: { id: string; name: string; emoji: string | null }
  /** Null for scheduled reminders (due soon, overdue). */
  actor: { id: string; displayName: string; avatarColor: string } | null
}

export interface NotificationPage {
  items: AppNotification[]
  nextCursor: string | null
}

export type DigestFrequency = 'Off' | 'Daily' | 'Weekly'

/** Which types the email digest includes. In-app notifications always show every type. */
export interface DigestTypes {
  mentioned: boolean
  assigned: boolean
  dueSoon: boolean
  overdue: boolean
  invited: boolean
  commentOnMyCard: boolean
  unblocked: boolean
}

export interface NotificationPreferences {
  digest: DigestFrequency
  types: DigestTypes
  /** Digests only go to confirmed email addresses. */
  emailConfirmed: boolean
}

export interface ListOptions {
  unread?: boolean
  cursor?: string | null
  limit?: number
}

function query(options: ListOptions) {
  const params = new URLSearchParams()
  if (options.unread) params.set('unread', 'true')
  if (options.cursor) params.set('cursor', options.cursor)
  if (options.limit) params.set('limit', String(options.limit))
  const text = params.toString()
  return text ? `?${text}` : ''
}

export const notificationsApi = {
  list: (options: ListOptions = {}) => api.get<NotificationPage>(`/notifications${query(options)}`),
  unreadCount: () => api.get<{ count: number }>('/notifications/unread-count'),
  markRead: (id: string) => api.post<void>(`/notifications/${id}/read`),
  markAllRead: () => api.post<void>('/notifications/read-all'),
  acceptInvite: (inviteId: string) =>
    api.post<{ boardId: string }>(`/me/invites/${inviteId}/accept`),
  preferences: () => api.get<NotificationPreferences>('/me/notification-preferences'),
  savePreferences: (prefs: Pick<NotificationPreferences, 'digest' | 'types'>) =>
    api.put<NotificationPreferences>('/me/notification-preferences', prefs),
}
