import { defineStore } from 'pinia'
import { ref } from 'vue'

import { ApiError } from '@/shared/api/http'

import { insightsApi, type InsightRange, type Insights } from './api'

/** One board's insights for one range; a newer load drops older answers. */
export const useInsightsStore = defineStore('insights', () => {
  const data = ref<Insights | null>(null)
  const loading = ref(false)
  const missing = ref(false)
  const failed = ref(false)
  let currentBoard: string | null = null
  let request = 0

  async function load(boardId: string, days: InsightRange) {
    // Keep the old range on screen while a new one loads; a different board starts blank.
    if (currentBoard !== boardId) data.value = null
    currentBoard = boardId
    const mine = ++request
    loading.value = true
    missing.value = false
    failed.value = false
    try {
      const result = await insightsApi.get(boardId, days)
      if (mine === request) data.value = result
    } catch (e) {
      if (mine !== request) return
      if (e instanceof ApiError && e.status === 404) missing.value = true
      else failed.value = true
    } finally {
      if (mine === request) loading.value = false
    }
  }

  return { data, loading, missing, failed, load }
})
