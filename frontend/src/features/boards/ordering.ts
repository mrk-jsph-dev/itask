import type { ListNeighbours } from './api'

/** Returns a copy of `items` with the item at `from` moved to index `to`. */
export function moveItem<T>(items: readonly T[], from: number, to: number): T[] {
  const copy = [...items]
  const [item] = copy.splice(from, 1)
  copy.splice(to, 0, item as T)
  return copy
}

/** Ids of the items on either side of `index` in an already reordered list. */
export function neighbours(items: readonly { id: string }[], index: number): ListNeighbours {
  return {
    beforeId: items[index - 1]?.id ?? null,
    afterId: items[index + 1]?.id ?? null,
  }
}
