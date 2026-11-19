import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { groupByDay } from '@/shared/ui/dates'
import { toast } from '@/shared/ui/toast'
import { notificationsApi, type AppNotification } from './api'

/** How often the unread count refreshes while the tab is visible. */
export const pollInterval = 60_000

/** How many notifications the bell's dropdown shows. */
export const latestLimit = 8

/**
 * Notifications: the unread count for the bell (polled), the dropdown's latest few, and the
 * paged list on the Notifications page. Reads are optimistic and roll back on error.
 */
export const useNotificationsStore = defineStore('notifications', () => {
  const unreadCount = ref(0)
  /** Null until the dropdown first opens. */
  const latest = ref<AppNotification[] | null>(null)
  /** The page's list; null while the first page loads. */
  const items = ref<AppNotification[] | null>(null)
  const unreadOnly = ref(false)
  const nextCursor = ref<string | null>(null)
  const loadingMore = ref(false)

  let request = 0
  let timer: ReturnType<typeof setInterval> | null = null

  const groups = computed(() => groupByDay(items.value ?? []))

  async function refreshCount() {
    const { count } = await notificationsApi.unreadCount()
    unreadCount.value = count
  }

  const onVisible = () => {
    if (document.visibilityState === 'visible') refreshCount().catch(() => {})
  }

  function startPolling() {
    stopPolling()
    refreshCount().catch(() => {})
    timer = setInterval(onVisible, pollInterval)
    document.addEventListener('visibilitychange', onVisible)
  }

  function stopPolling() {
    if (timer) clearInterval(timer)
    timer = null
    document.removeEventListener('visibilitychange', onVisible)
  }

  async function loadLatest() {
    const [page] = await Promise.all([
      notificationsApi.list({ limit: latestLimit }),
      refreshCount(),
    ])
    latest.value = page.items
  }

  async function load(unread = unreadOnly.value) {
    const mine = ++request
    unreadOnly.value = unread
    items.value = null
    nextCursor.value = null
    try {
      const page = await notificationsApi.list({ unread })
      if (mine !== request) return
      items.value = page.items
      nextCursor.value = page.nextCursor
    } catch (e) {
      if (mine === request) items.value = []
      throw e
    }
  }

  async function loadMore() {
    if (!nextCursor.value || loadingMore.value) return
    const mine = request
    loadingMore.value = true
    try {
      const page = await notificationsApi.list({
        unread: unreadOnly.value,
        cursor: nextCursor.value,
      })
      if (mine !== request) return
      items.value = [...(items.value ?? []), ...page.items]
      nextCursor.value = page.nextCursor
    } finally {
      loadingMore.value = false
    }
  }

  /** Sets `readAt` on every copy of the given notifications; returns an undo. */
  function stamp(ids: Set<string> | null, readAt: string | null) {
    const before = new Map<string, string | null>()
    for (const list of [latest.value, items.value]) {
      for (const n of list ?? []) {
        if (ids && !ids.has(n.id)) continue
        if (!before.has(n.id)) before.set(n.id, n.readAt)
        n.readAt = readAt ?? n.readAt
      }
    }
    return () => {
      for (const list of [latest.value, items.value]) {
        for (const n of list ?? []) if (before.has(n.id)) n.readAt = before.get(n.id) ?? null
      }
    }
  }

  async function markRead(n: AppNotification) {
    if (n.readAt) return
    const count = unreadCount.value
    const undo = stamp(new Set([n.id]), new Date().toISOString())
    n.readAt ??= new Date().toISOString()
    unreadCount.value = Math.max(0, count - 1)
    try {
      await notificationsApi.markRead(n.id)
    } catch {
      undo()
      n.readAt = null
      unreadCount.value = count
      toast("Couldn't mark it as read.", 'error')
    }
  }

  async function markAllRead() {
    const count = unreadCount.value
    const undo = stamp(null, new Date().toISOString())
    unreadCount.value = 0
    try {
      await notificationsApi.markAllRead()
    } catch {
      undo()
      unreadCount.value = count
      toast("Couldn't mark them as read.", 'error')
    }
  }

  /** Accepts an in-app invite and marks it read; returns the board to open. */
  async function acceptInvite(n: AppNotification, inviteId: string) {
    const { boardId } = await notificationsApi.acceptInvite(inviteId)
    await markRead(n)
    return boardId
  }

  return {
    unreadCount,
    latest,
    items,
    unreadOnly,
    nextCursor,
    loadingMore,
    groups,
    refreshCount,
    startPolling,
    stopPolling,
    loadLatest,
    load,
    loadMore,
    markRead,
    markAllRead,
    acceptInvite,
  }
})
