import { api } from '@/shared/api/http'

/** Server-side categories: the part of an activity type before the dot. */
export type ActivityCategory =
  | 'board'
  | 'member'
  | 'list'
  | 'card'
  | 'label'
  | 'checklist'
  | 'comment'
  | 'attachment'
  | 'automation'
  | 'doorway'

export interface ActivityEntry {
  id: string
  /** Dotted type such as `card.moved`. */
  type: string
  /** Names captured when the change happened; shape depends on `type`. */
  data: Record<string, unknown>
  createdAt: string
  cardId: string | null
  board: { id: string; name: string; emoji: string | null }
  actor: { id: string; displayName: string; avatarColor: string }
}

export interface ActivityPage {
  items: ActivityEntry[]
  nextCursor: string | null
}

export interface ActivityFilter {
  type?: ActivityCategory | null
  actorId?: string | null
  cursor?: string | null
}

export interface BoardMemberOption {
  userId: string
  displayName: string
  avatarColor: string
}

function query(filter: ActivityFilter) {
  const params = new URLSearchParams()
  if (filter.type) params.set('type', filter.type)
  if (filter.actorId) params.set('actorId', filter.actorId)
  if (filter.cursor) params.set('cursor', filter.cursor)
  const text = params.toString()
  return text ? `?${text}` : ''
}

export const activityApi = {
  mine: (filter: ActivityFilter = {}) =>
    api.get<ActivityPage>(`/me/activity${query({ ...filter, actorId: null })}`),
  board: (boardId: string, filter: ActivityFilter = {}) =>
    api.get<ActivityPage>(`/boards/${boardId}/activity${query(filter)}`),
  card: (boardId: string, cardId: string, cursor?: string | null) =>
    api.get<ActivityPage>(`/boards/${boardId}/cards/${cardId}/activity${query({ cursor })}`),
  boardName: (boardId: string) =>
    api.get<{ name: string; emoji: string | null }>(`/boards/${boardId}`),
  members: (boardId: string) => api.get<BoardMemberOption[]>(`/boards/${boardId}/members`),
}
