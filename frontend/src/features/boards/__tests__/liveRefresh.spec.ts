import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/shared/api/http'
import {
  cardsApi,
  commentsApi,
  listsApi,
  type BoardList,
  type Card,
  type CardDetail,
  type Comment,
} from '../api'
import { useCardDetailStore } from '../cardDetailStore'
import { useCardsStore } from '../cardsStore'
import { useListsStore } from '../listsStore'

const card = (id: string, listId: string, position: number, extra: Partial<Card> = {}): Card => ({
  id,
  listId,
  number: 1,
  key: `ITK-${id}`,
  title: id,
  emoji: null,
  position,
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

const list = (id: string, cards: Card[]): BoardList => ({
  id,
  name: id,
  emoji: null,
  position: 1,
  wipLimit: null,
  archivedAt: null,
  cards,
})

const detailOf = (c: Card, extra: Partial<CardDetail> = {}): CardDetail => ({
  card: c,
  description: null,
  createdById: 'u1',
  createdByName: 'Ada',
  createdAt: '2026-11-19T00:00:00Z',
  checklist: [],
  attachments: [],
  recurrence: null,
  ...extra,
})

const comment = (id: string, minute: number): Comment => ({
  id,
  authorId: 'u2',
  authorName: 'Bo',
  authorColor: '#000',
  body: id,
  createdAt: `2026-11-19T10:${String(minute).padStart(2, '0')}:00Z`,
  editedAt: null,
  mentions: [],
})

async function board() {
  vi.spyOn(listsApi, 'all').mockResolvedValue([
    list('todo', [card('a', 'todo', 1024), card('b', 'todo', 2048)]),
    list('done', [card('d', 'done', 1024)]),
  ])
  const lists = useListsStore()
  await lists.load('b1')
  return { lists, cards: useCardsStore() }
}

const ids = (lists: ReturnType<typeof useListsStore>) =>
  Object.fromEntries((lists.lists ?? []).map((l) => [l.id, l.cards.map((c) => c.id)]))

describe('live refresh', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('moves a card to its new list and position, idempotently', async () => {
    const { lists, cards } = await board()
    const moved = card('a', 'done', 512, { title: 'renamed' })

    cards.upsert(moved)
    cards.upsert(moved)

    expect(ids(lists)).toEqual({ todo: ['b'], done: ['a', 'd'] })
    expect(cards.find('a')!.list.cards[0]!.title).toBe('renamed')
  })

  it('adds new cards and drops archived ones', async () => {
    const { lists, cards } = await board()

    cards.upsert(card('n', 'todo', 1500))
    cards.upsert(card('d', 'done', 1024, { archivedAt: '2026-11-19T00:00:00Z' }))

    expect(ids(lists)).toEqual({ todo: ['a', 'n', 'b'], done: [] })
  })

  it('reloads a card from the API and drops it on 404', async () => {
    const { lists, cards } = await board()
    vi.spyOn(cardsApi, 'get')
      .mockResolvedValueOnce(detailOf(card('b', 'todo', 10)))
      .mockRejectedValueOnce(new ApiError(404, 'Not Found'))

    await cards.refresh('b')
    await cards.refresh('d')

    expect(ids(lists)).toEqual({ todo: ['b', 'a'], done: [] })
  })

  it('reloads lists without the loading state', async () => {
    const { lists } = await board()
    vi.spyOn(listsApi, 'all').mockResolvedValue([list('todo', [])])
    const pending = lists.refresh()

    expect(lists.lists).not.toBeNull()
    await pending
    expect(ids(lists)).toEqual({ todo: [] })
  })

  it('merges the newest comments into the open sheet', async () => {
    await board()
    const detail = useCardDetailStore()
    vi.spyOn(cardsApi, 'get').mockResolvedValue(detailOf(card('a', 'todo', 1024)))
    vi.spyOn(commentsApi, 'page')
      .mockResolvedValueOnce({ items: [comment('c2', 2), comment('c1', 1)], nextCursor: null })
      .mockResolvedValueOnce({ items: [comment('c3', 3), comment('c2', 2)], nextCursor: 'next' })
    await detail.open('b1', 'a')

    await detail.refresh('a', 'comments')

    expect(detail.comments.map((c) => c.id)).toEqual(['c1', 'c2', 'c3'])
    expect(detail.nextCursor).toBeNull()
  })

  it('ignores refreshes for a card that is not open', async () => {
    await board()
    const detail = useCardDetailStore()
    const get = vi.spyOn(cardsApi, 'get')

    await detail.refresh('a', 'card')
    expect(get).not.toHaveBeenCalled()
  })
})
