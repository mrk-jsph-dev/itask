import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope } from 'vue'

import { http } from '@/shared/api/http'
import { runToastAction, toasts } from '@/shared/ui/toast'
import { automationsApi } from '../api'
import { decodeRuns, RUNS_HEADER, useAutomationNotices } from '../notices'
import { useAutomationsStore } from '../store'

function encode(value: unknown) {
  const bytes = new TextEncoder().encode(JSON.stringify(value))
  return btoa(String.fromCharCode(...bytes))
}

const run = { runId: 'r1', ruleName: 'Finish flow', summary: 'Moved to Done', canUndo: true }

beforeEach(() => setActivePinia(createPinia()))
afterEach(() => vi.restoreAllMocks())

describe('automation notices', () => {
  it('decodes the header, including non-ASCII text', () => {
    expect(decodeRuns(encode([{ ...run, ruleName: 'Fertig ✅' }]))[0]!.ruleName).toBe('Fertig ✅')
    expect(decodeRuns('not base64!')).toEqual([])
    expect(decodeRuns(null)).toEqual([])
  })

  it('toasts runs on this board with an Undo that calls the API', async () => {
    const response = new Response('{}', { status: 200, headers: { [RUNS_HEADER]: encode([run]) } })
    Object.defineProperty(response, 'url', {
      value: 'http://localhost/api/boards/b1/cards/c1/complete',
    })
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(response)
    const undo = vi.spyOn(automationsApi, 'undo').mockResolvedValue(undefined)
    const changed = vi.fn<() => void>()
    const scope = effectScope()
    scope.run(() => useAutomationNotices(() => 'b1', changed))

    await http('/boards/b1/cards/c1/complete', { method: 'POST' })

    const toast = toasts.value[toasts.value.length - 1]!
    expect(toast.text).toBe('⚡ Moved to Done · Finish flow')
    expect(changed).toHaveBeenCalledTimes(1)
    runToastAction(toast.id)
    expect(undo).toHaveBeenCalledWith('b1', 'r1')
    scope.stop()
  })

  it('remembers the last card changed while recording and drafts from it', async () => {
    const response = new Response('{}', { status: 200 })
    Object.defineProperty(response, 'url', {
      value: 'http://localhost/api/boards/b1/cards/c7/labels',
    })
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(response)
    const fromRecording = vi.spyOn(automationsApi, 'fromRecording').mockResolvedValue({
      name: 'Review routine',
      trigger: { type: 'card.entered', listId: 'l3' },
      actions: [{ type: 'complete' }],
    })
    const store = useAutomationsStore()
    store.boardId = 'b1'
    const scope = effectScope()
    scope.run(() => useAutomationNotices(() => 'b1', vi.fn()))
    store.startRecording()

    await http('/boards/b1/cards/c7/labels', { method: 'PUT' })
    const draft = await store.stopRecording()

    expect(fromRecording).toHaveBeenCalledWith('b1', 'c7', expect.any(String))
    expect(draft).toMatchObject({ kind: 'Rule', name: 'Review routine', conditions: [] })
    expect(store.recording).toBeNull()
    scope.stop()
  })
})
