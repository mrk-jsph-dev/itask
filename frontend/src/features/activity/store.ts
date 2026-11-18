import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { activityApi, type ActivityCategory, type ActivityEntry } from './api'
import { groupByDay } from './format'

/** The activity page: the caller's own feed, or one board's feed when `boardId` is set. */
export const useActivityStore = defineStore('activity', () => {
  const boardId = ref<string | null>(null)
  const category = ref<ActivityCategory | null>(null)
  const actorId = ref<string | null>(null)
  /** Null while the first page loads. */
  const items = ref<ActivityEntry[] | null>(null)
  const nextCursor = ref<string | null>(null)
  const loadingMore = ref(false)

  // Responses from an older filter are dropped when the filter changed meanwhile.
  let request = 0

  const groups = computed(() => groupByDay(items.value ?? []))

  function fetchPage(cursor: string | null) {
    const filter = { type: category.value, actorId: actorId.value, cursor }
    return boardId.value ? activityApi.board(boardId.value, filter) : activityApi.mine(filter)
  }

  async function load() {
    const mine = ++request
    items.value = null
    nextCursor.value = null
    try {
      const page = await fetchPage(null)
      if (mine !== request) return
      items.value = page.items
      nextCursor.value = page.nextCursor
    } catch (e) {
      if (mine === request) items.value = []
      throw e
    }
  }

  async function open(board: string | null) {
    boardId.value = board
    category.value = null
    actorId.value = null
    await load()
  }

  async function setCategory(value: ActivityCategory | null) {
    category.value = value
    await load()
  }

  async function setActor(value: string | null) {
    actorId.value = value
    await load()
  }

  async function loadMore() {
    if (!nextCursor.value || loadingMore.value) return
    const mine = request
    loadingMore.value = true
    try {
      const page = await fetchPage(nextCursor.value)
      if (mine !== request) return
      items.value = [...(items.value ?? []), ...page.items]
      nextCursor.value = page.nextCursor
    } finally {
      loadingMore.value = false
    }
  }

  return {
    boardId,
    category,
    actorId,
    items,
    nextCursor,
    loadingMore,
    groups,
    open,
    load,
    setCategory,
    setActor,
    loadMore,
  }
})
