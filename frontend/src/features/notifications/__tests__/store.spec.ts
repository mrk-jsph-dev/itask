import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { notificationsApi } from '../api'
import { pollInterval, useNotificationsStore } from '../store'
import { notification } from './fixtures'

describe('notifications store', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('polls the unread count while the tab is visible and stops on request', async () => {
    vi.useFakeTimers()
    const count = vi.spyOn(notificationsApi, 'unreadCount').mockResolvedValue({ count: 3 })
    const store = useNotificationsStore()

    store.startPolling()
    await vi.advanceTimersByTimeAsync(0)
    expect(store.unreadCount).toBe(3)

    count.mockResolvedValue({ count: 5 })
    await vi.advanceTimersByTimeAsync(pollInterval)
    expect(store.unreadCount).toBe(5)
    expect(count).toHaveBeenCalledTimes(2)

    store.stopPolling()
    await vi.advanceTimersByTimeAsync(pollInterval * 3)
    expect(count).toHaveBeenCalledTimes(2)
  })

  it('loads the page, filters unread and pages on', async () => {
    const list = vi
      .spyOn(notificationsApi, 'list')
      .mockResolvedValueOnce({ items: [notification({ id: 'a' })], nextCursor: 'next' })
      .mockResolvedValueOnce({ items: [notification({ id: 'b' })], nextCursor: null })
    const store = useNotificationsStore()

    await store.load(true)
    await store.loadMore()

    expect(list).toHaveBeenNthCalledWith(1, { unread: true })
    expect(list).toHaveBeenNthCalledWith(2, { unread: true, cursor: 'next' })
    expect(store.items?.map((n) => n.id)).toEqual(['a', 'b'])
    expect(store.nextCursor).toBeNull()
  })

  it('marks read optimistically on every list holding the notification', async () => {
    vi.spyOn(notificationsApi, 'list').mockResolvedValue({
      items: [notification({ id: 'a' })],
      nextCursor: null,
    })
    vi.spyOn(notificationsApi, 'unreadCount').mockResolvedValue({ count: 1 })
    const read = vi.spyOn(notificationsApi, 'markRead').mockResolvedValue()
    const store = useNotificationsStore()
    await store.loadLatest()
    await store.load(false)

    await store.markRead(store.items![0]!)

    expect(read).toHaveBeenCalledWith('a')
    expect(store.unreadCount).toBe(0)
    expect(store.latest![0]!.readAt).not.toBeNull()
    expect(store.items![0]!.readAt).not.toBeNull()
  })

  it('rolls a failed read back', async () => {
    vi.spyOn(notificationsApi, 'list').mockResolvedValue({
      items: [notification({ id: 'a' })],
      nextCursor: null,
    })
    vi.spyOn(notificationsApi, 'markRead').mockRejectedValue(new Error('offline'))
    const store = useNotificationsStore()
    await store.load(false)
    store.unreadCount = 4

    await store.markRead(store.items![0]!)

    expect(store.unreadCount).toBe(4)
    expect(store.items![0]!.readAt).toBeNull()
  })

  it('marks all read and restores them when the call fails', async () => {
    vi.spyOn(notificationsApi, 'list').mockResolvedValue({
      items: [notification({ id: 'a' }), notification({ id: 'b', readAt: '2026-11-18T00:00:00Z' })],
      nextCursor: null,
    })
    const all = vi.spyOn(notificationsApi, 'markAllRead').mockRejectedValueOnce(new Error('x'))
    const store = useNotificationsStore()
    await store.load(false)
    store.unreadCount = 1

    await store.markAllRead()
    expect(store.unreadCount).toBe(1)
    expect(store.items!.map((n) => n.readAt)).toEqual([null, '2026-11-18T00:00:00Z'])

    all.mockResolvedValueOnce()
    await store.markAllRead()
    expect(store.unreadCount).toBe(0)
    expect(store.items!.every((n) => n.readAt)).toBe(true)
  })

  it('accepts an invite and marks it read', async () => {
    vi.spyOn(notificationsApi, 'acceptInvite').mockResolvedValue({ boardId: 'b9' })
    vi.spyOn(notificationsApi, 'markRead').mockResolvedValue()
    const store = useNotificationsStore()
    const n = notification({ type: 'invited', data: { inviteId: 'i1' } })
    store.unreadCount = 1

    expect(await store.acceptInvite(n, 'i1')).toBe('b9')
    expect(n.readAt).not.toBeNull()
    expect(store.unreadCount).toBe(0)
  })
})
