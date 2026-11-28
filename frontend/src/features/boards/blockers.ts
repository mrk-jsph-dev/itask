import type { Card } from './api'

/** Most matches the blocker picker shows. */
export const maxCandidates = 8

/**
 * Cards `cardId` could wait on: the board's other cards that aren't blockers yet, matching
 * `query` by key (e.g. "itk-4") or title, open ones first.
 */
export function blockerCandidates(
  cards: Card[],
  cardId: string,
  existing: string[],
  query: string,
): Card[] {
  const term = query.trim().toLowerCase()
  const taken = new Set([cardId, ...existing])
  return cards
    .filter((c) => !taken.has(c.id) && !c.archivedAt)
    .filter(
      (c) => !term || c.key.toLowerCase().startsWith(term) || c.title.toLowerCase().includes(term),
    )
    .sort((a, b) => Number(!!a.completedAt) - Number(!!b.completedAt) || a.number - b.number)
    .slice(0, maxCandidates)
}
