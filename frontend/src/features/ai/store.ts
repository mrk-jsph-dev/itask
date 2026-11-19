import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { ApiError } from '@/shared/api/http'
import { toast } from '@/shared/ui/toast'
import { aiApi, type AiStatus } from './api'

/** ProblemDetails title the API returns when AI is switched off. */
const AI_DISABLED = 'AI is not enabled.'

/** Message for a failed AI call: spent quota, AI switched off, or the model's answer was unusable. */
export function aiErrorText(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.status === 0 || e.status === 429 || e.status === 502 || e.status === 403) return e.message
    if (e.status === 404) return 'AI is not available.'
  }
  return "AI couldn't answer. Try again."
}

/**
 * AI availability and the daily quota for the signed-in user. Status is fetched once per user
 * (no polling); every AI answer updates `remaining`.
 */
export const useAiStore = defineStore('ai', () => {
  const status = ref<AiStatus | null>(null)
  const forUser = ref<string | null>(null)
  /** Name of the AI action in flight, e.g. 'quick-add'. */
  const busy = ref<string | null>(null)

  const enabled = computed(() => !!status.value?.enabled)
  const remaining = computed(() => status.value?.remaining ?? 0)
  const limit = computed(() => status.value?.limit ?? 0)

  async function ensureLoaded(userId: string | null | undefined) {
    if (!userId || forUser.value === userId) return
    forUser.value = userId
    try {
      status.value = await aiApi.status()
    } catch {
      status.value = null
      forUser.value = null
    }
  }

  function setRemaining(value: number) {
    if (status.value) status.value = { ...status.value, remaining: Math.max(0, value) }
  }

  /**
   * Runs one AI call: tracks `busy`, applies the returned quota, and on failure shows a toast
   * and returns null. 429 empties the quota; 404 means AI was switched off.
   */
  async function run<T extends { remaining: number }>(
    name: string,
    call: () => Promise<T>,
  ): Promise<T | null> {
    busy.value = name
    try {
      const result = await call()
      setRemaining(result.remaining)
      return result
    } catch (e) {
      if (e instanceof ApiError && e.status === 429) setRemaining(0)
      if (e instanceof ApiError && e.status === 404 && e.message === AI_DISABLED && status.value) {
        status.value = { ...status.value, enabled: false }
      }
      toast(aiErrorText(e), 'error')
      return null
    } finally {
      busy.value = null
    }
  }

  return { status, enabled, remaining, limit, busy, ensureLoaded, run }
})
