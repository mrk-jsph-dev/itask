import { defineStore } from 'pinia'
import { ref } from 'vue'

import { toast } from '@/shared/ui/toast'
import { cardsApi, type Card, type CardDetail, type CardPatch } from './api'
import { useCardsStore } from './cardsStore'
import { toApiParams, type BoardFilter, type SortKey } from './filters'
import { errorText } from './store'

/**
 * Filtered, sorted cards for the List and Calendar views, loaded from `GET /boards/{id}/cards`.
 * Edits made here also reach the board's lists, so switching back to Board shows them.
 */
export const useViewsStore = defineStore('views', () => {
  const cardsStore = useCardsStore()
  const boardId = ref<string | null>(null)
  /** Matching cards; null while loading. */
  const cards = ref<Card[] | null>(null)
  const truncated = ref(false)
  /** Bumped per load, so a slow older response can't overwrite a newer one. */
  let request = 0
  /** Card details fetched for the calendar preview, by card id. */
  let details = new Map<string, Promise<CardDetail>>()

  async function load(
    id: string,
    filter: BoardFilter,
    sort: SortKey,
    range?: { from: Date; to: Date },
  ) {
    const mine = ++request
    if (boardId.value !== id) cards.value = null
    boardId.value = id
    details = new Map()
    try {
      const result = await cardsApi.query(id, toApiParams(filter, sort, range))
      if (mine !== request) return
      cards.value = result.items
      truncated.value = result.truncated
    } catch (e) {
      if (mine !== request) return
      cards.value ??= []
      toast(errorText(e, "Couldn't load the tasks."), 'error')
    }
  }

  function reset() {
    request++
    details = new Map()
    boardId.value = null
    cards.value = null
    truncated.value = false
  }

  function replace(card: Card) {
    cards.value = (cards.value ?? []).map((c) => (c.id === card.id ? card : c))
  }

  /** Applies `patch` at once, saves it, and rolls back with a toast if the server refuses. */
  async function update(cardId: string, patch: CardPatch) {
    const before = cards.value?.find((c) => c.id === cardId)
    if (!before || !boardId.value) return
    replace({ ...before, ...patch } as Card)
    try {
      const saved = await cardsApi.update(boardId.value, cardId, patch)
      replace(saved)
      cardsStore.patch(cardId, saved)
    } catch (e) {
      replace(before)
      toast(errorText(e, "Couldn't update the card."), 'error')
    }
  }

  /** The card's detail (description, checklist), fetched once per load. */
  function detail(cardId: string): Promise<CardDetail> {
    let pending = details.get(cardId)
    if (!pending) {
      pending = cardsApi.get(boardId.value!, cardId)
      pending.catch(() => details.delete(cardId))
      details.set(cardId, pending)
    }
    return pending
  }

  /** Marks the card done or open again; rolls back with a toast on error. */
  async function toggleComplete(cardId: string) {
    const before = cards.value?.find((c) => c.id === cardId)
    if (!before || !boardId.value) return
    const completing = !before.completedAt
    replace({ ...before, completedAt: completing ? new Date().toISOString() : null })
    try {
      const saved = await (completing
        ? cardsApi.complete(boardId.value, cardId)
        : cardsApi.reopen(boardId.value, cardId))
      replace(saved)
      cardsStore.patch(cardId, saved)
    } catch (e) {
      replace(before)
      toast(errorText(e, "Couldn't update the card."), 'error')
    }
  }

  return { boardId, cards, truncated, load, reset, update, detail, toggleComplete }
})
