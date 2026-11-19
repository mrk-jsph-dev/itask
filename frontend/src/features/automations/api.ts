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
  /** Times it changed a card in the last 30 days. */
  runs: number
  /** Moves a block-mode doorway refused in the last 30 days. */
  blocked: number
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

export interface AutomationPreview {
  count: number
  samples: { cardId: string; key: string; title: string }[]
  /** `history` = replayed over the last 30 days; `now` = cards it would act on now. */
  basis: 'history' | 'now'
}

export interface RecordingDraft {
  name: string
  trigger: TriggerSpec
  actions: ActionSpec[]
}

export interface AutomationTally {
  ruleId: string
  name: string
  ran: number
  blocked: number
}

export interface AutomationSummary {
  days: number
  ran: number
  blocked: number
  sentence: string
  rules: AutomationTally[]
}

/** An automation drafted by AI; ids are already the board's own. */
export type AiAutomationDraft = Omit<AutomationInput, 'trigger'> & { trigger: TriggerSpec }

export interface AiSuggestion {
  reason: string
  automation: AiAutomationDraft
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
  preview: (boardId: string, input: AutomationInput) =>
    api.post<AutomationPreview>(`${base(boardId)}/preview`, input),
  fromRecording: (boardId: string, cardId: string, since: string) =>
    api.post<RecordingDraft>(`${base(boardId)}/from-recording`, { cardId, since }),
  summary: (boardId: string) => api.get<AutomationSummary>(`${base(boardId)}/summary`),
  aiDraft: (boardId: string, text: string) =>
    api.post<{ automation: AiAutomationDraft; remaining: number }>(
      `/boards/${boardId}/ai/automation`,
      {
        text,
      },
    ),
  aiReview: (boardId: string) =>
    api.post<{ suggestions: AiSuggestion[]; remaining: number }>(
      `/boards/${boardId}/ai/automation-review`,
    ),
}
