import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/shared/api/http'
import { toasts } from '@/shared/ui/toast'
import { aiApi } from '../api'
import { aiErrorText, useAiStore } from '../store'

describe('ai store', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.restoreAllMocks())

  it('loads the status once per user', async () => {
    const status = vi
      .spyOn(aiApi, 'status')
      .mockResolvedValue({ enabled: true, limit: 20, remaining: 18 })
    const ai = useAiStore()

    await ai.ensureLoaded('u1')
    await ai.ensureLoaded('u1')
    expect(status).toHaveBeenCalledTimes(1)
    expect([ai.enabled, ai.remaining, ai.limit]).toEqual([true, 18, 20])

    await ai.ensureLoaded('u2')
    expect(status).toHaveBeenCalledTimes(2)
  })

  it('applies the remaining quota from each answer and clears busy', async () => {
    vi.spyOn(aiApi, 'status').mockResolvedValue({ enabled: true, limit: 20, remaining: 18 })
    const ai = useAiStore()
    await ai.ensureLoaded('u1')

    const pending = ai.run('summary', async () => ({ summary: 'Done.', remaining: 17 }))
    expect(ai.busy).toBe('summary')
    const result = await pending

    expect(result?.summary).toBe('Done.')
    expect(ai.remaining).toBe(17)
    expect(ai.busy).toBeNull()
  })

  it('empties the quota on 429 and turns AI off when the API says it is disabled', async () => {
    vi.spyOn(aiApi, 'status').mockResolvedValue({ enabled: true, limit: 20, remaining: 3 })
    const ai = useAiStore()
    await ai.ensureLoaded('u1')

    const spent = await ai.run('digest', () =>
      Promise.reject(new ApiError(429, 'You have used all 20 AI requests for today.')),
    )
    expect(spent).toBeNull()
    expect(ai.remaining).toBe(0)
    expect(toasts.value[toasts.value.length - 1]?.text).toContain('used all 20')

    await ai.run('digest', () => Promise.reject(new ApiError(404, 'Not found.')))
    expect(ai.enabled).toBe(true)
    await ai.run('digest', () => Promise.reject(new ApiError(404, 'AI is not enabled.')))
    expect(ai.enabled).toBe(false)
  })

  it('words errors for the user', () => {
    expect(aiErrorText(new ApiError(502, 'The AI could not give a usable answer.'))).toBe(
      'The AI could not give a usable answer.',
    )
    expect(aiErrorText(new ApiError(404, 'AI is not enabled.'))).toBe('AI is not available.')
    expect(aiErrorText(new Error('boom'))).toBe("AI couldn't answer. Try again.")
  })
})
