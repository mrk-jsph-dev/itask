import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/shared/api/http'
import { toasts } from '@/shared/ui/toast'
import { cardsApi, type Card } from '../api'
import { useCardsStore } from '../cardsStore'
import { emptyFilter } from '../filters'
import { useViewsStore } from '../viewsStore'

const card = (id: string, extra: Partial<Card> = {}): Card => ({
  id,
  listId: 'l1',
  number: 1,
  key: `RM-${id}`,
  title: id,
  emoji: null,
  position: 1,
  dueAt: null,
  startAt: null,
  priority: 'None',
  coverColor: null,
  completedAt: null,
  archivedAt: null,
  labels: [],
  assignees: [],
  checklistDone: 0,
  checklistTotal: 0,
  commentCount: 0,
  attachmentCount: 0,
  recurring: false,
  rowVersion: 'rv',
  ...extra,
})

describe('views store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('loads with the filter as API params', async () => {
    const query = vi
      .spyOn(cardsApi, 'query')
      .mockResolvedValue({ items: [card('a')], truncated: true })
    const views = useViewsStore()

    await views.load('b1', { ...emptyFilter(), labels: ['x'] }, 'due')

    const params = query.mock.calls[0]![1]
    expect(query.mock.calls[0]![0]).toBe('b1')
    expect([params.get('labels'), params.get('sort')]).toEqual(['x', 'due'])
    expect(views.cards?.map((c) => c.id)).toEqual(['a'])
    expect(views.truncated).toBe(true)
  })

  it('keeps only the newest response', async () => {
    let resolveSlow: (v: { items: Card[]; truncated: boolean }) => void = () => {}
    vi.spyOn(cardsApi, 'query')
      .mockReturnValueOnce(new Promise((r) => (resolveSlow = r)))
      .mockResolvedValueOnce({ items: [card('new')], truncated: false })
    const views = useViewsStore()

    const slow = views.load('b1', emptyFilter(), 'position')
    await views.load('b1', emptyFilter(), 'title')
    resolveSlow({ items: [card('old')], truncated: false })
    await slow

    expect(views.cards?.map((c) => c.id)).toEqual(['new'])
  })

  it('updates optimistically and tells the board lists', async () => {
    vi.spyOn(cardsApi, 'query').mockResolvedValue({ items: [card('a')], truncated: false })
    const saved = card('a', { priority: 'High', rowVersion: 'rv2' })
    vi.spyOn(cardsApi, 'update').mockResolvedValue(saved)
    const views = useViewsStore()
    const patch = vi.spyOn(useCardsStore(), 'patch')
    await views.load('b1', emptyFilter(), 'position')

    const pending = views.update('a', { priority: 'High' })
    expect(views.cards![0]!.priority).toBe('High')
    await pending

    expect(views.cards![0]!.rowVersion).toBe('rv2')
    expect(patch).toHaveBeenCalledWith('a', saved)
  })

  it('rolls back and toasts when the update fails', async () => {
    vi.spyOn(cardsApi, 'query').mockResolvedValue({ items: [card('a')], truncated: false })
    vi.spyOn(cardsApi, 'update').mockRejectedValue(new ApiError(403, 'Forbidden'))
    const views = useViewsStore()
    await views.load('b1', emptyFilter(), 'position')

    await views.update('a', { dueAt: '2026-12-01T00:00:00.000Z' })

    expect(views.cards![0]!.dueAt).toBeNull()
    expect(toasts.value[toasts.value.length - 1]?.tone).toBe('error')
  })

  it('toggles completion and tells the board lists', async () => {
    vi.spyOn(cardsApi, 'query').mockResolvedValue({ items: [card('a')], truncated: false })
    const done = card('a', { completedAt: '2026-11-19T10:00:00Z' })
    const complete = vi.spyOn(cardsApi, 'complete').mockResolvedValue(done)
    const views = useViewsStore()
    const patch = vi.spyOn(useCardsStore(), 'patch')
    await views.load('b1', emptyFilter(), 'position')

    await views.toggleComplete('a')

    expect(complete).toHaveBeenCalledWith('b1', 'a')
    expect(views.cards![0]!.completedAt).toBe(done.completedAt)
    expect(patch).toHaveBeenCalledWith('a', done)
  })

  it('fetches a card detail once per load', async () => {
    vi.spyOn(cardsApi, 'query').mockResolvedValue({ items: [card('a')], truncated: false })
    const get = vi.spyOn(cardsApi, 'get').mockResolvedValue({
      card: card('a'),
      description: 'Notes',
      createdById: 'u1',
      createdByName: 'Ada',
      createdAt: '2026-11-19T10:00:00Z',
      checklist: [],
      attachments: [],
      recurrence: null,
    })
    const views = useViewsStore()
    await views.load('b1', emptyFilter(), 'position')

    await views.detail('a')
    await views.detail('a')
    await views.load('b1', emptyFilter(), 'position')
    await views.detail('a')

    expect(get).toHaveBeenCalledTimes(2)
  })
})
