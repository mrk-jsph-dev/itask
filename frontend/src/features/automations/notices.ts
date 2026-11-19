import { onScopeDispose } from 'vue'

import { onApiResponse } from '@/shared/api/http'
import { toast, toastWithUndo } from '@/shared/ui/toast'
import { automationsApi, type AutomationRunNotice } from './api'

export const RUNS_HEADER = 'X-ITask-Automations'

/** The runs in an `X-ITask-Automations` header value (base64 JSON); empty when it is missing or broken. */
export function decodeRuns(value: string | null): AutomationRunNotice[] {
  if (!value) return []
  try {
    const bytes = Uint8Array.from(atob(value), (c) => c.charCodeAt(0))
    const parsed = JSON.parse(new TextDecoder().decode(bytes)) as unknown
    return Array.isArray(parsed) ? (parsed as AutomationRunNotice[]) : []
  } catch {
    return []
  }
}

/**
 * Shows a toast for every automation run the API reports on this board's requests, with Undo
 * when the run can be undone, and calls `changed` so the page reloads what the run touched.
 */
export function useAutomationNotices(boardId: () => string, changed: () => void) {
  const stop = onApiResponse((response) => {
    const runs = decodeRuns(response.headers.get(RUNS_HEADER))
    if (runs.length === 0 || !response.url.includes(`/api/boards/${boardId()}/`)) return
    const board = boardId()
    for (const run of runs) {
      const text = `⚡ ${run.summary} · ${run.ruleName}`
      if (!run.canUndo) {
        toast(text)
        continue
      }
      toastWithUndo(text, () => {
        automationsApi
          .undo(board, run.runId)
          .then(() => {
            toast(`Undid ${run.ruleName}.`)
            changed()
          })
          .catch((e: unknown) =>
            toast(e instanceof Error ? e.message : "Couldn't undo it.", 'error'),
          )
      })
    }
    changed()
  })
  onScopeDispose(stop)
}
