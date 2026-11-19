import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/shared/api/http'
import { runToastAction, toasts } from '@/shared/ui/toast'
import {
  cardsApi,
  checklistApi,
  commentsApi,
  listsApi,
  type Card,
  type ChecklistItem,
  type Comment,
} from '../api'
import { useCardDetailStore } from '../cardDetailStore'
import { useListsStore } from '../listsStore'

const tile: Card = {
  id: 'c1',
  listId: 'l1',
  number: 1,
  key: 'ITK-1',
  title: 'Write spec',
  position: 1024,
  dueAt: null,
  startAt: null,
  priority: 'None',
  coverColor: null,
  completedAt: null,
  archivedAt: null,
  labels: [],
  assignees: [],
  checklistDone: 1,
  checklistTotal: 2,
  commentCount: 3,
  emoji: null,
  attachmentCount: 0,
  rowVersion: 'rv',
}

const item = (id: string, isDone = false): ChecklistItem => ({
  id,
  text: id.toUpperCase(),
  isDone,
  position: 1,
  dueAt: null,
  assigneeId: null,
})

const comment = (id: string): Comment => ({
  id,
  authorId: 'u1',
  authorName: 'Ada',
  authorColor: '#1D5BF5',
  body: `Comment ${id}`,
  createdAt: '2026-11-18T10:00:00Z',
  editedAt: null,
  mentions: [],
})

const lastToast = () => toasts.value[toasts.value.length - 1]

async function opened() {
  vi.spyOn(listsApi, 'all').mockResolvedValue([
    {
      id: 'l1',
      name: 'To do',
      emoji: null,
      position: 1,
      wipLimit: null,
      archivedAt: null,
      cards: [tile],
    },
  ])
  const lists = useListsStore()
  await lists.load('b1')
  vi.spyOn(cardsApi, 'get').mockResolvedValue({
    card: tile,
    description: 'Notes',
    createdById: 'u1',
    createdByName: 'Ada',
    createdAt: '2026-11-18T00:00:00Z',
    checklist: [item('a', true), item('b')],
    attachments: [],
  })
  // Newest first from the API.
  vi.spyOn(commentsApi, 'page').mockResolvedValue({
    items: [comment('3'), comment('2')],
    nextCursor: 'cur',
  })
  const detail = useCardDetailStore()
  await detail.open('b1', 'c1')
  return { detail, card: () => lists.lists![0]!.cards[0]! }
}

describe('card detail store', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('loads the card and shows comments oldest first', async () => {
    const { detail } = await opened()

    expect(detail.description).toBe('Notes')
    expect(detail.checklist.map((i) => i.id)).toEqual(['a', 'b'])
    expect(detail.comments.map((c) => c.id)).toEqual(['2', '3'])
    expect(detail.progress).toBe(50)
  })

  it('toggles optimistically and keeps the tile in step', async () => {
    const { detail, card } = await opened()
    vi.spyOn(checklistApi, 'setDone').mockResolvedValue({ ...item('b'), isDone: true })

    const pending = detail.toggleItem('b')

    expect(detail.checklist[1]!.isDone).toBe(true)
    expect(card().checklistDone).toBe(2)
    await pending
    expect(detail.done).toBe(2)
  })

  it('rolls a refused toggle back with a toast', async () => {
    const { detail, card } = await opened()
    vi.spyOn(checklistApi, 'setDone').mockRejectedValue(
      new ApiError(403, 'Your role does not allow this.'),
    )

    await detail.toggleItem('b')

    expect(detail.checklist[1]!.isDone).toBe(false)
    expect(card().checklistDone).toBe(1)
    expect(lastToast()?.tone).toBe('error')
  })

  it('adds items and comments and updates the tile counts', async () => {
    const { detail, card } = await opened()
    vi.spyOn(checklistApi, 'add').mockResolvedValue(item('c'))
    vi.spyOn(commentsApi, 'add').mockResolvedValue(comment('4'))

    await detail.addItem('C')
    await detail.addComment('Comment 4')

    expect(card().checklistTotal).toBe(3)
    expect(card().commentCount).toBe(4)
    expect(detail.comments[detail.comments.length - 1]?.id).toBe('4')
  })

  it('loads older comments above the current ones', async () => {
    const { detail } = await opened()
    vi.mocked(commentsApi.page).mockResolvedValue({ items: [comment('1')], nextCursor: null })

    await detail.loadOlder()

    expect(commentsApi.page).toHaveBeenLastCalledWith('b1', 'c1', 'cur')
    expect(detail.comments.map((c) => c.id)).toEqual(['1', '2', '3'])
    expect(detail.nextCursor).toBeNull()
  })

  it('moves an item and restores the order when the server refuses', async () => {
    const { detail } = await opened()
    const move = vi
      .spyOn(checklistApi, 'move')
      .mockRejectedValue(new ApiError(409, 'The checklist changed.'))

    await detail.moveChecklistItem(1, 0)

    expect(move).toHaveBeenCalledWith('b1', 'c1', 'b', { beforeId: null, afterId: 'a' })
    expect(detail.checklist.map((i) => i.id)).toEqual(['a', 'b'])
  })

  it('deletes after the undo window, and undo keeps the item', async () => {
    vi.useFakeTimers()
    const { detail, card } = await opened()
    const remove = vi.spyOn(checklistApi, 'remove').mockResolvedValue(undefined)

    detail.removeItem('a')
    expect(detail.checklist.map((i) => i.id)).toEqual(['b'])
    expect(card().checklistTotal).toBe(1)
    runToastAction(lastToast()!.id)
    await vi.advanceTimersByTimeAsync(5000)

    expect(remove).not.toHaveBeenCalled()
    expect(detail.checklist.map((i) => i.id)).toEqual(['a', 'b'])
    expect(card().checklistTotal).toBe(2)

    detail.removeItem('b')
    await vi.advanceTimersByTimeAsync(5000)
    expect(remove).toHaveBeenCalledWith('b1', 'c1', 'b')
  })
})
