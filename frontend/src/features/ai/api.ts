import { api } from '@/shared/api/http'

export type AiPriority = 'None' | 'Low' | 'Medium' | 'High' | 'Urgent'

/** `remaining` is the caller's AI requests left today; every AI answer carries it. */
export interface AiStatus {
  enabled: boolean
  limit: number
  remaining: number
}

export interface QuickAddDraft {
  title: string
  /** Calendar day (YYYY-MM-DD) in the user's time zone. */
  dueDate: string | null
  priority: AiPriority
  labelIds: string[]
  listId: string | null
  remaining: number
}

export interface BreakdownDraft {
  items: string[]
  remaining: number
}

export interface SummaryDraft {
  summary: string
  remaining: number
}

export interface BoardDigest {
  digest: string
  due: number
  stuck: number
  done: number
  remaining: number
}

export interface GoalCard {
  list: string
  title: string
  checklist: string[]
}

export interface GoalBoardDraft {
  name: string
  emoji: string | null
  lists: string[]
  cards: GoalCard[]
  remaining: number
}

const card = (boardId: string, cardId: string) => `/boards/${boardId}/cards/${cardId}/ai`

export const aiApi = {
  status: () => api.get<AiStatus>('/ai/status'),
  quickAdd: (boardId: string, text: string) =>
    api.post<QuickAddDraft>(`/boards/${boardId}/ai/quick-add`, { text }),
  breakdown: (boardId: string, cardId: string) =>
    api.post<BreakdownDraft>(`${card(boardId, cardId)}/breakdown`),
  summary: (boardId: string, cardId: string) =>
    api.post<SummaryDraft>(`${card(boardId, cardId)}/summary`),
  digest: (boardId: string) => api.post<BoardDigest>(`/boards/${boardId}/ai/digest`),
  boardFromGoal: (goal: string) => api.post<GoalBoardDraft>('/ai/board-from-goal', { goal }),
}
