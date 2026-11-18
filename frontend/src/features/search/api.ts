import { api } from '@/shared/api/http'

export interface BoardHit {
  id: string
  name: string
  emoji: string | null
  color: string
}

export interface CardHit {
  id: string
  boardId: string
  key: string
  title: string
  boardName: string
  boardEmoji: string | null
  listName: string
  completedAt: string | null
  /** The query was this card's key (e.g. `ITK-42`). */
  keyMatch: boolean
}

export interface CommentHit {
  id: string
  cardId: string
  boardId: string
  cardKey: string
  cardTitle: string
  authorName: string
  createdAt: string
  /** Plain-text excerpt around the match. */
  snippet: string
}

export interface SearchResults {
  boards: BoardHit[]
  cards: CardHit[]
  comments: CommentHit[]
}

/** Shortest query the server accepts. */
export const minQueryLength = 2

export const searchApi = {
  search: (q: string, limit = 20) =>
    api.get<SearchResults>(`/search?q=${encodeURIComponent(q)}&limit=${limit}`),
}
