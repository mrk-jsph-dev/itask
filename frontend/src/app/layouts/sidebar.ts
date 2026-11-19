import { onBeforeUnmount, onMounted, ref } from 'vue'

/** `full` sidebar from 1280 px, an icon `rail` from 768 px, below that a `sheet` behind a menu button. */
export type ShellMode = 'full' | 'rail' | 'sheet'

export function shellMode(width: number): ShellMode {
  if (width >= 1280) return 'full'
  if (width >= 768) return 'rail'
  return 'sheet'
}

/** The shell mode for the current window width, updated on resize. */
export function useShellMode() {
  const mode = ref<ShellMode>(shellMode(window.innerWidth))
  const update = () => (mode.value = shellMode(window.innerWidth))
  onMounted(() => window.addEventListener('resize', update))
  onBeforeUnmount(() => window.removeEventListener('resize', update))
  return mode
}

/** Most boards the sidebar lists. */
export const sidebarBoardLimit = 8

/** Starred boards first, then the rest, each keeping their order; capped at `limit`. */
export function sidebarBoards<T extends { isStarred: boolean }>(
  boards: readonly T[],
  limit = sidebarBoardLimit,
): T[] {
  return [...boards.filter((b) => b.isStarred), ...boards.filter((b) => !b.isStarred)].slice(
    0,
    limit,
  )
}

/** Route names that show a board, so the sidebar lists its members. */
export const boardRouteNames = ['board', 'card', 'board-activity'] as const
