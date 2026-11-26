import { api } from '@/shared/api/http'

export const ranges = [30, 60, 90] as const
export type InsightRange = (typeof ranges)[number]

export interface DayCount {
  /** Local date `yyyy-MM-dd`. */
  date: string
  count: number
}

export interface CycleTime {
  count: number
  medianDays: number | null
  p85Days: number | null
}

export interface ListTime {
  listId: string
  name: string
  emoji: string | null
  medianDays: number | null
  samples: number
}

export interface AgingCard {
  id: string
  key: string
  title: string
  listId: string
  listName: string
  days: number
}

export interface ListWip {
  listId: string
  name: string
  emoji: string | null
  open: number
  limit: number | null
}

export interface Insights {
  days: InsightRange
  from: string
  to: string
  throughput: DayCount[]
  created: DayCount[]
  cycleTime: CycleTime
  timeInList: ListTime[]
  aging: AgingCard[]
  wip: ListWip[]
}

export const insightsApi = {
  boardName: (boardId: string) =>
    api.get<{ name: string; emoji: string | null }>(`/boards/${boardId}`),
  get: (
    boardId: string,
    days: InsightRange,
    timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone,
  ) => {
    const params = new URLSearchParams({ days: String(days) })
    if (timeZone) params.set('tz', timeZone)
    return api.get<Insights>(`/boards/${boardId}/insights?${params}`)
  },
}
