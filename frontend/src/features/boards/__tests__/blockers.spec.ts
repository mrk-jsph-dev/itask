import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/shared/api/http'
import { toasts } from '@/shared/ui/toast'
import { cardsApi, commentsApi, listsApi, type Card } from '../api'
import { blockerCandidates, maxCandidates } from '../blockers'
import { useCardDetailStore } from '../cardDetailStore'
import BlockedChip from '../components/BlockedChip.vue'
import CardBlockers from '../components/CardBlockers.vue'
import { emptyFilter, matches, parseQuery, toApiParams, toQuery } from '../filters'
import { emptyPlan, planFor } from '../live'
import { useListsStore } from '../listsStore'

const card = (id: string, number: number, title: string, extra: Partial<Card> = {}): Card => ({
  id,
  listId: 'l1',
  number,
  key: `ITK-${number}`,
  title,
  emoji: null,
  position: number,
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
  rowVersion: 'rv',
  ...extra,
})

describe('blockerCandidates', () => {
  const cards = [
    card('a', 1, 'Ship release'),
    card('b', 2, 'Write tests', { completedAt: '2026-11-20T00:00:00Z' }),
    card('c', 3, 'Review copy'),
    card('d', 4, 'Old idea', { archivedAt: '2026-11-20T00:00:00Z' }),
    card('e', 12, 'Plan launch'),
  ]

  it('leaves out the card itself, existing blockers and archived cards; open ones first', () => {
    expect(blockerCandidates(cards, 'a', ['c'], '').map((c) => c.id)).toEqual(['e', 'b'])
  })

  it('matches keys by prefix and titles by substring', () => {
    expect(blockerCandidates(cards, 'a', [], 'itk-1').map((c) => c.id)).toEqual(['e'])
    expect(blockerCandidates(cards, 'a', [], 'TEST').map((c) => c.id)).toEqual(['b'])
  })

  it('shows at most a handful', () => {
    const many = Array.from({ length: 20 }, (_, i) => card(`x${i}`, i + 10, `Task ${i}`))
    expect(blockerCandidates(many, 'a', [], '')).toHaveLength(maxCandidates)
  })
})

describe('BlockedChip', () => {
  it('shows 🔒 with the open blocker count only when blocked', () => {
    const blocked = mount(BlockedChip, {
      props: { card: { blocked: true, blockerCount: 2, blockingCount: 0 } },
    })
    expect(blocked.get('[data-testid=blocked-chip]').text()).toBe('🔒2')
    expect(blocked.get('[data-testid=blocked-chip]').attributes('aria-label')).toBe(
      'Blocked by 2 cards',
    )

    const free = mount(BlockedChip, {
      props: { card: { blocked: false, blockerCount: 0, blockingCount: 1 } },
    })
    expect(free.find('[data-testid=blocked-chip]').exists()).toBe(false)
    expect(free.text()).toBe('')
  })

  it('shows "blocks N" on request', () => {
    const wrapper = mount(BlockedChip, {
      props: { card: { blocked: false, blockerCount: 0, blockingCount: 3 }, showBlocking: true },
    })
    expect(wrapper.text()).toBe('blocks 3')
  })
})

describe('blocked filter', () => {
  it('round-trips through the URL and the API params', () => {
    const state = parseQuery({ blocked: 'yes' })
    expect(state.filter.blocked).toBe('yes')
    expect(toQuery(state)).toEqual({ blocked: 'yes' })
    expect(toApiParams(state.filter, 'position', undefined, '').get('blocked')).toBe('yes')
    expect(parseQuery({ blocked: 'maybe' }).filter.blocked).toBe('')
  })

  it('matches like the API', () => {
    const blocked = card('a', 1, 'A', { blocked: true, blockerCount: 1 })
    const free = card('b', 2, 'B')
    const yes = { ...emptyFilter(), blocked: 'yes' as const }
    const no = { ...emptyFilter(), blocked: 'no' as const }
    expect([matches(blocked, yes), matches(free, yes)]).toEqual([true, false])
    expect([matches(blocked, no), matches(free, no)]).toEqual([false, true])
  })
})

describe('live plan', () => {
  it('reloads the lists when a dependency changes, so the blocker count follows', () => {
    const plan = emptyPlan()
    planFor(
      {
        type: 'card.dependency_added',
        boardId: 'b1',
        cardId: 'a',
        listId: null,
        position: null,
        userId: null,
        actorId: 'u2',
        at: '2026-11-19T00:00:00Z',
      },
      plan,
    )
    expect(plan.lists).toBe(true)
    expect([...plan.cards]).toEqual(['a'])
  })
})

describe('blockers in the card sheet', () => {
  let a: Card
  let b: Card

  beforeEach(async () => {
    setActivePinia(createPinia())
    a = card('a', 1, 'Ship release')
    b = card('b', 2, 'Write tests')
    vi.spyOn(listsApi, 'all').mockResolvedValue([
      { id: 'l1', name: 'To do', emoji: null, position: 1, wipLimit: null, archivedAt: null, cards: [a, b] },
    ])
    await useListsStore().load('b1')
    vi.spyOn(cardsApi, 'get').mockResolvedValue({
      card: a,
      description: null,
      createdById: 'u1',
      createdByName: 'Ada',
      createdAt: '2026-11-18T00:00:00Z',
      checklist: [],
      attachments: [],
      recurrence: null,
      blockedBy: [],
    })
    vi.spyOn(commentsApi, 'page').mockResolvedValue({ items: [], nextCursor: null })
    await useCardDetailStore().open('b1', 'a')
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  const tile = (id: string) => useListsStore().lists![0]!.cards.find((c) => c.id === id)!

  it('adds a blocker from the picker and updates both tiles', async () => {
    const add = vi
      .spyOn(cardsApi, 'addBlocker')
      .mockResolvedValue({ ...a, blocked: true, blockerCount: 1 })
    const wrapper = mount(CardBlockers, { props: { cardId: 'a', editable: true } })

    await wrapper.get('button').trigger('click')
    const options = wrapper.findAll('[role=option]')
    expect(options.map((o) => o.text())).toEqual(['ITK-2Write tests'])
    await wrapper.get('input').setValue('write')
    await wrapper.get('input').trigger('keydown', { key: 'Enter' })
    await vi.waitFor(() => expect(add).toHaveBeenCalledWith('b1', 'a', 'b'))

    await vi.waitFor(() => expect(wrapper.findAll('[aria-label=Blockers] li')).toHaveLength(1))
    expect(tile('a').blocked).toBe(true)
    expect(tile('b').blockingCount).toBe(1)
  })

  it('toasts the server message for a loop', async () => {
    vi.spyOn(cardsApi, 'addBlocker').mockRejectedValue(
      new ApiError(409, 'That would make a loop: the other card already waits on this one.', undefined, undefined, {
        code: 'dependency_cycle',
      }),
    )
    const wrapper = mount(CardBlockers, { props: { cardId: 'a', editable: true } })

    await wrapper.get('button').trigger('click')
    await wrapper.get('[role=option]').trigger('mousedown')

    await vi.waitFor(() => expect(toasts.value[toasts.value.length - 1]?.text).toMatch(/loop/))
    expect(wrapper.findAll('[aria-label=Blockers] li')).toHaveLength(0)
  })

  it('removes a blocker', async () => {
    useCardDetailStore().blockedBy = [
      { id: 'b', key: 'ITK-2', title: 'Write tests', completed: false, archived: false },
    ]
    const remove = vi.spyOn(cardsApi, 'removeBlocker').mockResolvedValue(a)
    const wrapper = mount(CardBlockers, { props: { cardId: 'a', editable: true } })

    await wrapper.get('[aria-label="Remove blocker ITK-2"]').trigger('click')

    expect(remove).toHaveBeenCalledWith('b1', 'a', 'b')
    expect(wrapper.findAll('[aria-label=Blockers] li')).toHaveLength(0)
  })

  it('is read-only for viewers', () => {
    useCardDetailStore().blockedBy = [
      { id: 'b', key: 'ITK-2', title: 'Write tests', completed: true, archived: false },
    ]
    const wrapper = mount(CardBlockers, { props: { cardId: 'a', editable: false } })

    expect(wrapper.find('button').exists()).toBe(false)
    expect(wrapper.text()).toContain('Write tests')
    expect(wrapper.text()).toContain('(done)')
  })
})
