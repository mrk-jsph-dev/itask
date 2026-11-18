import { api } from '@/shared/api/http'

export type BoardRole = 'Viewer' | 'Editor' | 'Owner'

export interface BoardSummary {
  id: string
  name: string
  emoji: string | null
  role: BoardRole
}

export const boardsApi = {
  mine: () => api.get<BoardSummary[]>('/boards'),
  get: (id: string) => api.get<BoardSummary>(`/boards/${id}`),
  create: (name: string, emoji?: string) => api.post<BoardSummary>('/boards', { name, emoji }),
}
