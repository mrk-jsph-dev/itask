import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { activityApi, type ActivityEntry, type ActivityPage } from '../api'
import { useActivityStore } from '../store'

const entry = (id: string, createdAt = '2026-11-18T10:00:00Z'): ActivityEntry => ({
  id,
  type: 'card.created',
  data: { card: { key: 'ITK-1', title: 'Card' } },
  createdAt,
  cardId: 'c1',
  board: { id: 'b1', name: 'Board', emoji: null },
  actor: { id: 'u1', displayName: 'Ada', avatarColor: '#1D5BF5' },
})

const page = (ids: string[], nextCursor: string | null = null): ActivityPage => ({
  items: ids.map((id) => entry(id)),
  nextCursor,
})

describe('activity store', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.restoreAllMocks())

  it('loads my feed without a board', async () => {
    const mine = vi.spyOn(activityApi, 'mine').mockResolvedValue(page(['a', 'b']))
    const store = useActivityStore()

    await store.open(null)

    expect(mine).toHaveBeenCalledWith({ type: null, actorId: null, cursor: null })
    expect(store.items?.map((i) => i.id)).toEqual(['a', 'b'])
    expect(store.groups).toHaveLength(1)
  })

  it('loads a board feed and resets filters when the board changes', async () => {
    const board = vi.spyOn(activityApi, 'board').mockResolvedValue(page(['a']))
    const store = useActivityStore()
    await store.open('b1')
    await store.setCategory('comment')
    await store.setActor('u2')

    expect(board).toHaveBeenLastCalledWith('b1', { type: 'comment', actorId: 'u2', cursor: null })

    await store.open('b2')
    expect(board).toHaveBeenLastCalledWith('b2', { type: null, actorId: null, cursor: null })
    expect(store.category).toBeNull()
  })

  it('appends the next page with the same filter', async () => {
    const board = vi
      .spyOn(activityApi, 'board')
      .mockResolvedValueOnce(page(['a', 'b'], 'cur'))
      .mockResolvedValueOnce(page(['c']))
    const store = useActivityStore()
    await store.open('b1')
    await store.loadMore()

    expect(board).toHaveBeenLastCalledWith('b1', { type: null, actorId: null, cursor: 'cur' })
    expect(store.items?.map((i) => i.id)).toEqual(['a', 'b', 'c'])
    expect(store.nextCursor).toBeNull()

    await store.loadMore()
    expect(board).toHaveBeenCalledTimes(2)
  })

  it('drops a response that arrives after the filter changed', async () => {
    let resolveOld: (p: ActivityPage) => void = () => {}
    vi.spyOn(activityApi, 'board')
      .mockReturnValueOnce(new Promise((r) => (resolveOld = r)))
      .mockResolvedValueOnce(page(['new']))
    const store = useActivityStore()

    const first = store.open('b1')
    await store.setCategory('card')
    resolveOld(page(['old']))
    await first

    expect(store.items?.map((i) => i.id)).toEqual(['new'])
  })

  it('shows an empty feed and rethrows when loading fails', async () => {
    vi.spyOn(activityApi, 'mine').mockRejectedValue(new Error('down'))
    const store = useActivityStore()

    await expect(store.open(null)).rejects.toThrow('down')
    expect(store.items).toEqual([])
  })
})
