import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAutomationsStore } from '@/features/automations/store'
import { ApiError } from '@/shared/api/http'
import { runToastAction, toasts } from '@/shared/ui/toast'
import { cardsApi, labelsApi, listsApi, type BoardList, type Card } from '../api'
import { useCardsStore } from '../cardsStore'
import { useListsStore } from '../listsStore'

const card = (title: string, listId: string): Card => ({
  id: title.toLowerCase(),
  listId,
  number: 1,
  key: `ITK-${title}`,
  title,
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
  blocked: false,
  blockerCount: 0,
  blockingCount: 0,
  rowVersion: `rv-${title}`,
})

const list = (id: string, titles: string[]): BoardList => ({
  id,
  name: id,
  emoji: null,
  position: 1,
  wipLimit: null,
  archivedAt: null,
  cards: titles.map((t) => card(t, id)),
})

const lastToast = () => toasts.value[toasts.value.length - 1]

async function loaded() {
  vi.spyOn(listsApi, 'all').mockResolvedValue([list('todo', ['A', 'B', 'C']), list('done', ['D'])])
  const lists = useListsStore()
  await lists.load('b1')
  return { lists, cards: useCardsStore() }
}

const titles = (lists: ReturnType<typeof useListsStore>) =>
  Object.fromEntries((lists.lists ?? []).map((l) => [l.id, l.cards.map((c) => c.title)]))

describe('cards store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('moves to another list at once and sends the target and neighbours', async () => {
    const move = vi.spyOn(cardsApi, 'move').mockImplementation(async (_b, id) => ({
      card: { ...card('A', 'done'), id },
      renumbered: false,
    }))
    const { lists, cards } = await loaded()

    await cards.move('a', 'done', 0)

    expect(titles(lists)).toEqual({ todo: ['B', 'C'], done: ['A', 'D'] })
    expect(move).toHaveBeenCalledWith(
      'b1',
      'a',
      { listId: 'done', beforeId: null, afterId: 'd' },
      'rv-A',
      false,
    )
  })

  it('moves within a list', async () => {
    const move = vi
      .spyOn(cardsApi, 'move')
      .mockResolvedValue({ card: card('C', 'todo'), renumbered: false })
    const { lists, cards } = await loaded()

    await cards.move('c', 'todo', 0)

    expect(titles(lists).todo).toEqual(['C', 'A', 'B'])
    expect(move.mock.calls[0]![2]).toEqual({ listId: 'todo', beforeId: null, afterId: 'a' })
  })

  it('rolls back and reloads on a 409', async () => {
    vi.spyOn(cardsApi, 'move').mockRejectedValue(new ApiError(409, 'The card was changed.'))
    const { lists, cards } = await loaded()

    await cards.move('a', 'done', 1)

    expect(titles(lists)).toEqual({ todo: ['A', 'B', 'C'], done: ['D'] })
    expect(lastToast()).toMatchObject({ text: 'The card was changed.', tone: 'error' })
    expect(listsApi.all).toHaveBeenCalledTimes(2)
  })

  it('hands a doorway refusal to the automations store instead of toasting', async () => {
    vi.spyOn(cardsApi, 'move').mockRejectedValue(
      new ApiError(409, 'Done needs an assignee first.', undefined, undefined, {
        code: 'doorway',
        rule: 'Gate',
        unmet: ['an assignee'],
      }),
    )
    const { lists, cards } = await loaded()
    const toasts = lastToast()

    await cards.move('a', 'done', 1)

    expect(titles(lists)).toEqual({ todo: ['A', 'B', 'C'], done: ['D'] })
    expect(useAutomationsStore().refused).toMatchObject({
      cardId: 'a',
      listId: 'done',
      rule: 'Gate',
      unmet: ['an assignee'],
    })
    expect(lastToast()).toBe(toasts)
  })

  it('restores the pre-drag order when a drop fails', async () => {
    vi.spyOn(cardsApi, 'move').mockRejectedValue(new ApiError(403, 'Not allowed.'))
    const { lists, cards } = await loaded()

    cards.beginDrag()
    lists.lists![0]!.cards = lists.lists![0]!.cards.filter((c) => c.id !== 'b')
    lists.lists![1]!.cards = [...lists.lists![1]!.cards, card('B', 'todo')]
    await cards.commitDrag('b')

    expect(titles(lists)).toEqual({ todo: ['A', 'B', 'C'], done: ['D'] })
  })

  it('ignores moves that change nothing', async () => {
    const move = vi.spyOn(cardsApi, 'move')
    const { cards } = await loaded()

    await cards.move('a', 'todo', -1)
    await cards.move('d', 'done', 5)

    expect(move).not.toHaveBeenCalled()
  })

  it('archives with an Undo that restores the card at the end', async () => {
    vi.spyOn(cardsApi, 'archive').mockResolvedValue({ ...card('A', 'todo'), archivedAt: 'x' })
    const restore = vi.spyOn(cardsApi, 'restore').mockResolvedValue(card('A', 'todo'))
    const { lists, cards } = await loaded()

    await cards.archive('a')
    expect(titles(lists).todo).toEqual(['B', 'C'])

    runToastAction(lastToast()!.id)
    await vi.waitFor(() => expect(titles(lists).todo).toEqual(['B', 'C', 'A']))
    expect(restore).toHaveBeenCalledWith('b1', 'a')
  })

  it('appends a created card to its list', async () => {
    vi.spyOn(cardsApi, 'create').mockResolvedValue(card('E', 'done'))
    const { lists, cards } = await loaded()

    await cards.create('done', 'E')

    expect(titles(lists).done).toEqual(['D', 'E'])
  })

  it('reloads the board after completing a repeating card so its next copy shows', async () => {
    const { lists, cards } = await loaded()
    const repeating = { ...lists.lists![0]!.cards[0]!, recurring: true }
    vi.spyOn(cardsApi, 'complete').mockResolvedValue({
      ...repeating,
      recurring: false,
      completedAt: '2030-03-04T10:00:00Z',
    })
    const reload = vi
      .spyOn(listsApi, 'all')
      .mockResolvedValue([list('todo', ['A', 'B', 'C', 'A2']), list('done', ['D'])])

    await cards.toggleComplete(repeating)

    expect(reload).toHaveBeenCalled()
    expect(titles(lists).todo).toEqual(['A', 'B', 'C', 'A2'])
    expect(lastToast()?.text).toContain('Next one')
  })

  it('renames and removes labels on cards too', async () => {
    vi.spyOn(labelsApi, 'all').mockResolvedValue([
      { id: 'l1', name: 'Bug', color: '#E11D48', emoji: null },
    ])
    vi.spyOn(labelsApi, 'update').mockResolvedValue({
      id: 'l1',
      name: 'Defect',
      color: '#E11D48',
      emoji: null,
    })
    vi.spyOn(labelsApi, 'remove').mockResolvedValue(undefined)
    const { lists, cards } = await loaded()
    await cards.loadLabels('b1')
    lists.lists![0]!.cards[0] = { ...lists.lists![0]!.cards[0]!, labels: [cards.labels![0]!] }

    await cards.updateLabel('l1', { name: 'Defect', color: '#E11D48', emoji: null })
    expect(lists.lists![0]!.cards[0]!.labels.map((l) => l.name)).toEqual(['Defect'])

    await cards.removeLabel('l1')
    expect(lists.lists![0]!.cards[0]!.labels).toEqual([])
    expect(cards.labels).toEqual([])
  })
})
