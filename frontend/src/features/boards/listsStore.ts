import { defineStore } from 'pinia'
import { ref } from 'vue'

import { toast, toastWithUndo } from '@/shared/ui/toast'
import { listsApi, type BoardList, type ListInput } from './api'
import { moveItem, neighbours } from './ordering'
import { errorText } from './store'

export const useListsStore = defineStore('lists', () => {
  const boardId = ref<string | null>(null)
  /** Active lists of the open board in order; null while loading. */
  const lists = ref<BoardList[] | null>(null)
  /** Archived lists, loaded on demand. */
  const archived = ref<BoardList[] | null>(null)

  async function load(id: string) {
    boardId.value = id
    lists.value = null
    archived.value = null
    const loaded = await listsApi.all(id)
    if (boardId.value === id) lists.value = loaded
  }

  async function loadArchived() {
    archived.value = await listsApi.archived(boardId.value!)
  }

  async function create(input: ListInput) {
    const created = await listsApi.create(boardId.value!, input)
    lists.value = [...(lists.value ?? []), { ...created, archivedAt: null }]
  }

  async function rename(listId: string, input: ListInput) {
    const saved = await listsApi.rename(boardId.value!, listId, input)
    lists.value = (lists.value ?? []).map((l) => (l.id === listId ? { ...l, ...saved } : l))
  }

  /**
   * Saves a move that is already applied to `lists` (drag and drop reorders the array itself).
   * Rolls the order back and shows a toast if the server rejects it.
   */
  async function commitMove(from: number, to: number) {
    const current = lists.value
    if (!current || from === to) return
    const moved = current[to]!
    try {
      await listsApi.move(boardId.value!, moved.id, neighbours(current, to))
    } catch (e) {
      if (lists.value) lists.value = moveItem(lists.value, to, from)
      toast(errorText(e, "Couldn't move the list."), 'error')
    }
  }

  /** Moves the list at `from` to index `to` at once, then saves it. */
  async function move(from: number, to: number) {
    const current = lists.value
    if (!current || to < 0 || to >= current.length || from === to) return
    lists.value = moveItem(current, from, to)
    await commitMove(from, to)
  }

  /** Hides the list at once; the toast offers Undo for 5 s. */
  async function archive(listId: string) {
    const current = lists.value ?? []
    const index = current.findIndex((l) => l.id === listId)
    const list = current[index]
    if (!list) return
    lists.value = current.filter((l) => l.id !== listId)
    try {
      await listsApi.archive(boardId.value!, listId)
    } catch (e) {
      lists.value = [...(lists.value ?? [])]
      lists.value.splice(index, 0, list)
      toast(errorText(e, "Couldn't archive the list."), 'error')
      return
    }
    if (archived.value)
      archived.value = [{ ...list, archivedAt: new Date().toISOString() }, ...archived.value]
    toastWithUndo(`Archived "${list.name}".`, () => void restore(listId))
  }

  /** Brings an archived list back at the end of the board. */
  async function restore(listId: string) {
    const id = boardId.value!
    try {
      await listsApi.restore(id, listId)
    } catch (e) {
      toast(errorText(e, "Couldn't restore the list."), 'error')
      return
    }
    if (archived.value) archived.value = archived.value.filter((l) => l.id !== listId)
    const loaded = await listsApi.all(id)
    if (boardId.value === id) lists.value = loaded
  }

  return {
    boardId,
    lists,
    archived,
    load,
    loadArchived,
    create,
    rename,
    commitMove,
    move,
    archive,
    restore,
  }
})
