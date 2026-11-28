import { defineStore } from 'pinia'
import { ref } from 'vue'

import { useAutomationsStore, doorwayRefusal } from '@/features/automations/store'
import { ApiError } from '@/shared/api/http'
import { toast, toastWithUndo } from '@/shared/ui/toast'
import {
  cardsApi,
  labelsApi,
  type BoardList,
  type Card,
  type CardPatch,
  type Label,
  type LabelInput,
} from './api'
import { byName } from './options'
import { neighbours } from './ordering'
import { useListsStore } from './listsStore'
import { errorText } from './store'

/** Where a card sits: its list's index in the board and its own index in that list. */
interface Spot {
  list: BoardList
  index: number
}

/**
 * Cards of the open board. Cards live inside `useListsStore().lists`, so list reloads and
 * card changes share one source of truth; this store holds the card actions and board labels.
 */
export const useCardsStore = defineStore('cards', () => {
  const listsStore = useListsStore()
  /** Board labels by name; null while loading. */
  const labels = ref<Label[] | null>(null)
  /** Card order before a drag started, restored if the server rejects the drop. */
  let snapshot: Map<string, Card[]> | null = null

  const boardId = () => listsStore.boardId!

  function find(cardId: string): Spot | null {
    for (const list of listsStore.lists ?? []) {
      const index = list.cards.findIndex((c) => c.id === cardId)
      if (index >= 0) return { list, index }
    }
    return null
  }

  /** Replaces a card in place, or drops it when `card` is archived. */
  function put(card: Card) {
    const spot = find(card.id)
    if (!spot) return
    const cards = [...spot.list.cards]
    if (card.archivedAt) cards.splice(spot.index, 1)
    else cards[spot.index] = card
    spot.list.cards = cards
  }

  /** Merges `change` into one card, e.g. to keep tile counts in step with the detail sheet. */
  function patch(cardId: string, change: Partial<Card>) {
    const spot = find(cardId)
    if (spot) put({ ...spot.list.cards[spot.index]!, ...change })
  }

  function takeSnapshot() {
    snapshot = new Map((listsStore.lists ?? []).map((l) => [l.id, [...l.cards]]))
  }

  function restoreSnapshot() {
    if (!snapshot) return
    for (const list of listsStore.lists ?? []) {
      const saved = snapshot.get(list.id)
      if (saved) list.cards = saved
    }
    snapshot = null
  }

  async function reloadLists() {
    await listsStore.load(boardId()).catch(() => undefined)
  }

  /** True while a drag or move waits for the server; live refreshes hold off until then. */
  function busy() {
    return snapshot !== null
  }

  /**
   * Puts `card` where the server has it: in its list by position. Archived cards, and cards
   * whose list isn't shown, leave the board. Applying the same card twice changes nothing.
   */
  function upsert(card: Card) {
    for (const list of listsStore.lists ?? []) {
      if ((list.id !== card.listId || card.archivedAt) && list.cards.some((c) => c.id === card.id))
        list.cards = list.cards.filter((c) => c.id !== card.id)
    }
    if (card.archivedAt) return
    const target = listsStore.lists?.find((l) => l.id === card.listId)
    if (!target) return
    const cards = target.cards.filter((c) => c.id !== card.id)
    const at = cards.findIndex((c) => c.position > card.position)
    cards.splice(at < 0 ? cards.length : at, 0, card)
    target.cards = cards
  }

  /** Reloads one card after someone else changed it; a card that's gone leaves the board. */
  async function refresh(cardId: string) {
    const id = boardId()
    try {
      const detail = await cardsApi.get(id, cardId)
      if (listsStore.boardId === id) upsert(detail.card)
    } catch (e) {
      if (!(e instanceof ApiError && e.status === 404) || listsStore.boardId !== id) return
      for (const list of listsStore.lists ?? [])
        if (list.cards.some((c) => c.id === cardId))
          list.cards = list.cards.filter((c) => c.id !== cardId)
    }
  }

  /** Reloads the board labels without the loading state. */
  async function refreshLabels() {
    const id = boardId()
    const loaded = await labelsApi.all(id)
    if (listsStore.boardId === id) labels.value = loaded
  }

  async function loadLabels(id: string) {
    labels.value = null
    const loaded = await labelsApi.all(id)
    if (listsStore.boardId === id) labels.value = loaded
  }

  async function create(listId: string, title: string) {
    const card = await cardsApi.create(boardId(), listId, title)
    const list = listsStore.lists?.find((l) => l.id === listId)
    if (list) list.cards = [...list.cards, card]
    return card
  }

  async function update(cardId: string, patch: CardPatch) {
    put(await cardsApi.update(boardId(), cardId, patch))
  }

  async function setLabels(cardId: string, labelIds: string[]) {
    put(await cardsApi.setLabels(boardId(), cardId, labelIds))
  }

  async function setAssignees(cardId: string, userIds: string[]) {
    put(await cardsApi.setAssignees(boardId(), cardId, userIds))
  }

  /** Completing a repeating card makes the server add its next copy, so the board reloads. */
  async function toggleComplete(card: Card) {
    const spawns = card.recurring && !card.completedAt
    try {
      put(
        await (card.completedAt
          ? cardsApi.reopen(boardId(), card.id)
          : cardsApi.complete(boardId(), card.id)),
      )
      // New copies and the cards waiting on this one are elsewhere on the board.
      if (spawns || card.blockingCount) await reloadLists()
      if (spawns) toast('🔁 Next one is on the board.')
    } catch (e) {
      toast(errorText(e, "Couldn't update the card."), 'error')
    }
  }

  /** Hides the card at once; the toast offers Undo for 5 s. */
  async function archive(cardId: string) {
    const spot = find(cardId)
    if (!spot) return
    const card = spot.list.cards[spot.index]!
    spot.list.cards = spot.list.cards.filter((c) => c.id !== cardId)
    try {
      await cardsApi.archive(boardId(), cardId)
    } catch (e) {
      const cards = [...spot.list.cards]
      cards.splice(spot.index, 0, card)
      spot.list.cards = cards
      toast(errorText(e, "Couldn't archive the card."), 'error')
      return
    }
    if (card.blockingCount) await reloadLists()
    toastWithUndo(`Archived "${card.title}".`, () => void restore(card))
  }

  /** Brings an archived card back at the end of its list. */
  async function restore(card: Card) {
    try {
      const restored = await cardsApi.restore(boardId(), card.id)
      const list = listsStore.lists?.find((l) => l.id === restored.listId)
      if (list) list.cards = [...list.cards, restored]
      if (restored.blockingCount) await reloadLists()
    } catch (e) {
      toast(errorText(e, "Couldn't restore the card."), 'error')
    }
  }

  /**
   * Saves a card's current spot (already applied to the lists) to the server. On error the
   * order before the change comes back; a 409 also reloads, since the board moved on. A
   * doorway's refusal is shown by the automations feature instead of a toast.
   */
  async function commit(cardId: string, overrideDoorway = false) {
    const spot = find(cardId)
    if (!spot) return
    const card = spot.list.cards[spot.index]!
    try {
      const { card: saved, renumbered } = await cardsApi.move(
        boardId(),
        cardId,
        { listId: spot.list.id, ...neighbours(spot.list.cards, spot.index) },
        card.rowVersion,
        overrideDoorway,
      )
      snapshot = null
      put(saved)
      if (renumbered) await reloadLists()
    } catch (e) {
      restoreSnapshot()
      const refusal = doorwayRefusal(e)
      if (refusal) {
        useAutomationsStore().refused = { ...refusal, cardId, listId: spot.list.id, index: spot.index }
        return
      }
      toast(errorText(e, "Couldn't move the card."), 'error')
      if (e instanceof ApiError && e.status === 409) await reloadLists()
    }
  }

  /** Call when a drag starts, before the lists change. */
  function beginDrag() {
    takeSnapshot()
  }

  /** Call when a card dropped somewhere new; the drag already reordered the lists. */
  async function commitDrag(cardId: string) {
    await commit(cardId)
  }

  /** Keyboard move: puts the card at `index` of `listId` (clamped), then saves it. */
  async function move(cardId: string, listId: string, index: number, overrideDoorway = false) {
    const from = find(cardId)
    const target = listsStore.lists?.find((l) => l.id === listId)
    if (!from || !target) return
    const card = from.list.cards[from.index]!
    const rest = target.cards.filter((c) => c.id !== cardId)
    const to = Math.max(0, Math.min(index, rest.length))
    if (from.list.id === listId && from.index === to) return
    takeSnapshot()
    if (from.list.id !== listId) from.list.cards = from.list.cards.filter((c) => c.id !== cardId)
    rest.splice(to, 0, { ...card, listId })
    target.cards = rest
    await commit(cardId, overrideDoorway)
  }

  async function createLabel(input: LabelInput) {
    const created = await labelsApi.create(boardId(), input)
    labels.value = [...(labels.value ?? []), created].sort(byName)
  }

  async function updateLabel(labelId: string, input: LabelInput) {
    const saved = await labelsApi.update(boardId(), labelId, input)
    labels.value = (labels.value ?? []).map((l) => (l.id === labelId ? saved : l)).sort(byName)
    eachCard((c) =>
      c.labels.some((l) => l.id === labelId)
        ? { ...c, labels: c.labels.map((l) => (l.id === labelId ? saved : l)).sort(byName) }
        : c,
    )
  }

  async function removeLabel(labelId: string) {
    await labelsApi.remove(boardId(), labelId)
    labels.value = (labels.value ?? []).filter((l) => l.id !== labelId)
    eachCard((c) =>
      c.labels.some((l) => l.id === labelId)
        ? { ...c, labels: c.labels.filter((l) => l.id !== labelId) }
        : c,
    )
  }

  function eachCard(change: (card: Card) => Card) {
    for (const list of listsStore.lists ?? []) list.cards = list.cards.map(change)
  }

  return {
    labels,
    find,
    patch,
    busy,
    upsert,
    refresh,
    refreshLabels,
    loadLabels,
    create,
    update,
    setLabels,
    setAssignees,
    toggleComplete,
    archive,
    restore,
    beginDrag,
    commitDrag,
    move,
    createLabel,
    updateLabel,
    removeLabel,
  }
})
