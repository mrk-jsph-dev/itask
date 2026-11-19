import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { BoardEvent } from '@/shared/realtime/boardHub'
import { createLiveQueue, emptyPlan, fullPlan, planFor, runPlan, type LiveActions } from '../live'

const event = (type: string, cardId: string | null = null): BoardEvent => ({
  type,
  boardId: 'b1',
  cardId,
  listId: null,
  position: null,
  userId: null,
  actorId: 'u2',
  at: '2026-11-19T00:00:00Z',
})

function actions(busy = false) {
  return {
    busy: vi.fn(() => busy),
    reloadLists: vi.fn(async () => {}),
    refreshCard: vi.fn(async () => {}),
    reloadLabels: vi.fn(async () => {}),
    reloadBoard: vi.fn(async () => {}),
    refreshDetail: vi.fn(async () => {}),
    reloadView: vi.fn(),
    gone: vi.fn(),
  } satisfies LiveActions
}

describe('planFor', () => {
  it('reloads one card, its sheet and the view for card events', () => {
    const plan = emptyPlan()
    planFor(event('card.moved', 'c1'), plan)
    planFor(event('comment.added', 'c2'), plan)

    expect([...plan.cards]).toEqual(['c1', 'c2'])
    expect(plan.detail.get('c1')).toEqual(new Set(['card']))
    expect(plan.detail.get('c2')).toEqual(new Set(['comments']))
    expect(plan.view && !plan.lists).toBe(true)
  })

  it('reloads the lists for list and label events', () => {
    const lists = emptyPlan()
    planFor(event('list.renamed'), lists)
    const labels = emptyPlan()
    planFor(event('label.deleted'), labels)

    expect(lists.lists).toBe(true)
    expect(labels.lists && labels.labels).toBe(true)
  })

  it('reloads the board for board and member events', () => {
    const joined = emptyPlan()
    planFor(event('member.joined'), joined)
    const removed = emptyPlan()
    planFor(event('member.removed'), removed)

    expect(joined.board && !joined.lists).toBe(true)
    expect(removed.board && removed.lists).toBe(true)
  })

  it('marks a deleted board as gone', () => {
    const plan = emptyPlan()
    planFor(event('board.deleted'), plan)
    expect(plan.gone).toBe(true)
  })
})

describe('runPlan', () => {
  it('skips single cards when the lists reload anyway', async () => {
    const a = actions()
    const plan = emptyPlan()
    planFor(event('card.updated', 'c1'), plan)
    planFor(event('list.created'), plan)

    await runPlan(plan, a)

    expect(a.reloadLists).toHaveBeenCalledOnce()
    expect(a.refreshCard).not.toHaveBeenCalled()
    expect(a.refreshDetail).toHaveBeenCalledWith('c1', 'card')
    expect(a.reloadView).toHaveBeenCalledOnce()
  })

  it('reloads everything on a resync', async () => {
    const a = actions()
    await runPlan(fullPlan('c9'), a)

    expect(a.reloadLists).toHaveBeenCalled()
    expect(a.reloadLabels).toHaveBeenCalled()
    expect(a.reloadBoard).toHaveBeenCalled()
    expect(a.refreshDetail).toHaveBeenCalledWith('c9', 'comments')
  })

  it('still reloads the view when a reload fails', async () => {
    const a = actions()
    a.refreshCard.mockRejectedValue(new Error('offline'))
    const plan = emptyPlan()
    planFor(event('card.updated', 'c1'), plan)

    await runPlan(plan, a)
    expect(a.reloadView).toHaveBeenCalled()
  })
})

describe('live queue', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('coalesces a burst into one reload per card', async () => {
    const a = actions()
    const queue = createLiveQueue(a)
    queue.push(event('card.updated', 'c1'))
    queue.push(event('card.labels_changed', 'c1'))
    queue.push(event('card.moved', 'c1'))

    await vi.advanceTimersByTimeAsync(150)
    expect(a.refreshCard).toHaveBeenCalledTimes(1)
  })

  it('waits while a drag is in progress', async () => {
    const a = actions(true)
    const queue = createLiveQueue(a)
    queue.push(event('card.moved', 'c1'))

    await vi.advanceTimersByTimeAsync(150)
    expect(a.refreshCard).not.toHaveBeenCalled()

    a.busy.mockReturnValue(false)
    await vi.advanceTimersByTimeAsync(500)
    expect(a.refreshCard).toHaveBeenCalledWith('c1')
  })

  it('drops queued work on stop', async () => {
    const a = actions()
    const queue = createLiveQueue(a)
    queue.push(event('card.moved', 'c1'))
    queue.stop()

    await vi.advanceTimersByTimeAsync(1000)
    expect(a.refreshCard).not.toHaveBeenCalled()
  })
})
