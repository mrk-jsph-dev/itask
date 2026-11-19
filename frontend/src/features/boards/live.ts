import { onScopeDispose, ref, watch, type Ref } from 'vue'

import {
  boardHub as defaultHub,
  type BoardEvent,
  type BoardHub,
  type PresenceUser,
} from '@/shared/realtime/boardHub'

/** What a batch of live events asks the open board to reload. */
export interface LivePlan {
  /** Cards to reload one by one; skipped when `lists` reloads everything anyway. */
  cards: Set<string>
  lists: boolean
  labels: boolean
  board: boolean
  /** Open card sheet parts to reload, by card id. */
  detail: Map<string, Set<'card' | 'comments'>>
  /** The List or Calendar view's own copy of the cards. */
  view: boolean
  /** The board is gone or the user lost access. */
  gone: boolean
}

export function emptyPlan(): LivePlan {
  return {
    cards: new Set(),
    lists: false,
    labels: false,
    board: false,
    detail: new Map(),
    view: false,
    gone: false,
  }
}

function detail(plan: LivePlan, cardId: string, part: 'card' | 'comments') {
  const parts = plan.detail.get(cardId) ?? new Set()
  parts.add(part)
  plan.detail.set(cardId, parts)
}

/** Adds what `e` needs reloaded to `plan`. Events only carry ids, so everything is refetched. */
export function planFor(e: BoardEvent, plan: LivePlan) {
  const category = e.type.split('.')[0]
  if (e.type === 'board.deleted') {
    plan.gone = true
    return
  }
  switch (category) {
    case 'card':
    case 'checklist':
    case 'attachment':
    case 'comment':
    case 'doorway':
      if (!e.cardId) break
      plan.cards.add(e.cardId)
      plan.view = true
      detail(plan, e.cardId, category === 'comment' ? 'comments' : 'card')
      break
    case 'list':
      plan.lists = true
      plan.view = true
      break
    case 'label':
      plan.labels = true
      plan.lists = true
      plan.view = true
      break
    case 'board':
      plan.board = true
      break
    case 'automation':
      // The board reload also reloads its automations; an undo also changed a card.
      plan.board = true
      if (e.cardId) {
        plan.cards.add(e.cardId)
        plan.view = true
        detail(plan, e.cardId, 'card')
      }
      break
    case 'member':
      plan.board = true
      // Leaving a board clears the member's assignments.
      if (e.type !== 'member.joined' && e.type !== 'member.role_changed') plan.lists = true
      break
  }
}

/** What the board page does with a plan. */
export interface LiveActions {
  /** A drag or move is waiting for the server; reloading now would undo it on screen. */
  busy: () => boolean
  reloadLists: () => Promise<unknown>
  refreshCard: (cardId: string) => Promise<unknown>
  reloadLabels: () => Promise<unknown>
  reloadBoard: () => Promise<unknown>
  refreshDetail: (cardId: string, part: 'card' | 'comments') => Promise<unknown>
  reloadView: () => void
  gone: () => void
}

/** Plan that reloads everything the board shows, after missed events. */
export function fullPlan(openCardId: string | null): LivePlan {
  const plan = { ...emptyPlan(), lists: true, labels: true, board: true, view: true }
  if (openCardId) plan.detail.set(openCardId, new Set(['card', 'comments']))
  return plan
}

export async function runPlan(plan: LivePlan, actions: LiveActions) {
  if (plan.gone) {
    actions.gone()
    return
  }
  const jobs: Promise<unknown>[] = []
  if (plan.lists) jobs.push(actions.reloadLists())
  else for (const id of plan.cards) jobs.push(actions.refreshCard(id))
  if (plan.labels) jobs.push(actions.reloadLabels())
  if (plan.board) jobs.push(actions.reloadBoard())
  for (const [id, parts] of plan.detail)
    for (const part of parts) jobs.push(actions.refreshDetail(id, part))
  // A failed reload leaves the board as it was; the next event or resync tries again.
  await Promise.allSettled(jobs)
  if (plan.view) actions.reloadView()
}

/**
 * Collects events for `delayMs` and runs one plan for the lot, so a burst (a bulk change, a
 * resync) costs one round of requests. Waits while `actions.busy()`.
 */
export function createLiveQueue(actions: LiveActions, delayMs = 150) {
  let plan = emptyPlan()
  let timer: ReturnType<typeof setTimeout> | undefined
  let running = false

  function schedule(ms = delayMs) {
    clearTimeout(timer)
    timer = setTimeout(() => void flush(), ms)
  }

  async function flush() {
    if (running || actions.busy()) {
      schedule(500)
      return
    }
    const current = plan
    plan = emptyPlan()
    running = true
    try {
      await runPlan(current, actions)
    } finally {
      running = false
    }
  }

  function push(e: BoardEvent) {
    planFor(e, plan)
    schedule()
  }

  function pushPlan(extra: LivePlan) {
    plan = merge(plan, extra)
    schedule()
  }

  function stop() {
    clearTimeout(timer)
    plan = emptyPlan()
  }

  return { push, pushPlan, stop }
}

function merge(a: LivePlan, b: LivePlan): LivePlan {
  const detailParts = new Map(a.detail)
  for (const [id, parts] of b.detail)
    detailParts.set(id, new Set([...(a.detail.get(id) ?? []), ...parts]))
  return {
    cards: new Set([...a.cards, ...b.cards]),
    lists: a.lists || b.lists,
    labels: a.labels || b.labels,
    board: a.board || b.board,
    detail: detailParts,
    view: a.view || b.view,
    gone: a.gone || b.gone,
  }
}

/**
 * Keeps the board page in sync with other people's changes while it is open, and reports
 * who else has it open.
 */
export function useBoardLive(
  boardId: Ref<string>,
  openCardId: () => string | null,
  actions: LiveActions,
  hub: BoardHub = defaultHub,
) {
  const presence = ref<PresenceUser[]>([])
  let queue = createLiveQueue(actions)

  watch(
    boardId,
    (id) => {
      queue.stop()
      queue = createLiveQueue(actions)
      presence.value = []
      hub.open(id, {
        event: (e) => queue.push(e),
        presence: (users) => (presence.value = users),
        accessLost: actions.gone,
        resync: () => queue.pushPlan(fullPlan(openCardId())),
      })
    },
    { immediate: true },
  )

  onScopeDispose(() => {
    queue.stop()
    hub.close()
  })

  return { status: hub.status, presence }
}
