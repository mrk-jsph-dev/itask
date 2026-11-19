import { api } from '@/shared/api/http'

export type AutomationKind = 'Rule' | 'Doorway'
export type DoorwayDirection = 'Enter' | 'Leave'
export type DoorwayMode = 'Block' | 'Fix' | 'Warn'
export type Priority = 'None' | 'Low' | 'Medium' | 'High' | 'Urgent'

export interface TriggerSpec {
  type: string
  listId?: string | null
  labelId?: string | null
  days?: number | null
  mentions?: boolean | null
}

/** A filter for rules, a requirement for doorways. */
export interface ConditionSpec {
  type: string
  labelIds?: string[] | null
  priority?: Priority | null
  userId?: string | null
}

export interface ActionSpec {
  type: string
  listId?: string | null
  position?: 'top' | 'bottom' | null
  labelId?: string | null
  who?: string | null
  userId?: string | null
  priority?: Priority | null
  days?: number | null
  items?: string[] | null
  text?: string | null
}

export interface Automation {
  id: string
  kind: AutomationKind
  name: string
  listId: string | null
  direction: DoorwayDirection | null
  mode: DoorwayMode | null
  trigger: TriggerSpec
  conditions: ConditionSpec[]
  actions: ActionSpec[]
  enabled: boolean
  /** `instant` runs inside the change; `hourly` may run late while the app sleeps. */
  timing: 'instant' | 'hourly'
  /** Runs in the last 30 days. */
  runs: number
  lastRunAt: string | null
  /** Why it can't run as it is (it refers to something gone). */
  problem: string | null
  createdAt: string
  rowVersion: string
}

export interface AutomationInput {
  kind: AutomationKind
  name: string
  listId: string | null
  direction: DoorwayDirection | null
  mode: DoorwayMode | null
  trigger: TriggerSpec | null
  conditions: ConditionSpec[]
  actions: ActionSpec[]
}

/** One run the API reports in the `X-ITask-Automations` header. */
export interface AutomationRunNotice {
  runId: string
  ruleName: string
  summary: string
  canUndo: boolean
}

const base = (boardId: string) => `/boards/${boardId}/automations`

export const automationsApi = {
  all: (boardId: string) => api.get<Automation[]>(base(boardId)),
  create: (boardId: string, input: AutomationInput) => api.post<Automation>(base(boardId), input),
  update: (boardId: string, id: string, input: Omit<AutomationInput, 'kind'>, rowVersion: string) =>
    api.put<Automation>(`${base(boardId)}/${id}`, { ...input, rowVersion }),
  toggle: (boardId: string, id: string, enabled: boolean) =>
    api.put<Automation>(`${base(boardId)}/${id}/enabled`, { enabled }),
  remove: (boardId: string, id: string) => api.del(`${base(boardId)}/${id}`),
  undo: (boardId: string, runId: string) =>
    api.post<unknown>(`/boards/${boardId}/automation-runs/${runId}/undo`),
}
