import { computed, type Ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { parseQuery, toQuery, views, type BoardState, type BoardViewKind } from './filters'

const viewKey = (boardId: string) => `itask.view.${boardId}`

/** The view last picked on this board, or null when none is stored (or storage is blocked). */
export function storedView(boardId: string): BoardViewKind | null {
  try {
    const value = localStorage.getItem(viewKey(boardId))
    return (views as readonly string[]).includes(value ?? '') ? (value as BoardViewKind) : null
  } catch {
    return null
  }
}

function storeView(boardId: string, view: BoardViewKind) {
  try {
    localStorage.setItem(viewKey(boardId), view)
  } catch {
    // Storage blocked: the URL still carries the view.
  }
}

/**
 * The board page's view, filter, sort, group and calendar month, kept in the route query so a
 * reload or a shared link shows the same thing. The view falls back to the one last picked on
 * this board, then Board.
 */
export function useBoardState(boardId: Ref<string>) {
  const route = useRoute()
  const router = useRouter()

  const parsed = computed(() => parseQuery(route.query))
  const view = computed<BoardViewKind>(
    () => parsed.value.view ?? storedView(boardId.value) ?? 'board',
  )
  const state = computed<BoardState>(() => ({ ...parsed.value, view: view.value }))

  function set(change: Partial<BoardState>) {
    const next = { ...state.value, ...change }
    if (change.view) storeView(boardId.value, change.view)
    void router.replace({ query: toQuery(next) })
  }

  return { state, view, set }
}
