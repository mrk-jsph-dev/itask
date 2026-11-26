import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import type { PaletteItem, PaletteSection } from '@/shared/ui/commandPalette'

import { minQueryLength, searchApi, type SearchResults } from './api'

/** A palette row plus where it leads. Recent items are stored in this shape. */
export interface SearchItem extends PaletteItem {
  to:
    | { name: 'board'; params: { boardId: string } }
    | { name: 'card'; params: { boardId: string; cardId: string } }
    | { name: 'board-insights' | 'board-activity'; params: { boardId: string } }
}

/** Board pages offered while a board is open: everything with an empty query, else the matching ones. */
export function boardCommands(boardId: string | null, query: string): SearchItem[] {
  if (!boardId) return []
  const commands: SearchItem[] = [
    {
      id: `cmd:insights:${boardId}`,
      icon: '📈',
      title: 'Insights',
      subtitle: 'Throughput, cycle time and stuck cards on this board',
      to: { name: 'board-insights', params: { boardId } },
    },
    {
      id: `cmd:activity:${boardId}`,
      icon: '⚡',
      title: 'Activity',
      subtitle: 'Recent changes on this board',
      to: { name: 'board-activity', params: { boardId } },
    },
  ]
  const text = query.trim().toLowerCase()
  return text ? commands.filter((c) => c.title.toLowerCase().includes(text)) : commands
}

export const debounceMs = 200
export const maxRecent = 8

const recentKey = (userId: string) => `itask.search.recent.${userId}`

function readRecent(userId: string): SearchItem[] {
  try {
    const raw = localStorage.getItem(recentKey(userId))
    const parsed: unknown = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? (parsed as SearchItem[]).slice(0, maxRecent) : []
  } catch {
    return []
  }
}

function writeRecent(userId: string, items: SearchItem[]) {
  try {
    localStorage.setItem(recentKey(userId), JSON.stringify(items))
  } catch {
    // Storage can be full or blocked; recents are a convenience only.
  }
}

/** Palette sections for a response: cards whose key matched come first, then Boards, Cards, Comments. */
export function toSections(results: SearchResults): PaletteSection<SearchItem>[] {
  const card = (c: SearchResults['cards'][number]): SearchItem => ({
    id: `card:${c.id}`,
    icon: c.completedAt ? '✅' : '🗂️',
    title: c.title,
    subtitle: `${c.boardEmoji ?? '📋'} ${c.boardName} · ${c.listName}`,
    hint: c.key,
    to: { name: 'card', params: { boardId: c.boardId, cardId: c.id } },
  })

  const sections: PaletteSection<SearchItem>[] = [
    { title: 'Key match', items: results.cards.filter((c) => c.keyMatch).map(card) },
    {
      title: 'Boards',
      items: results.boards.map((b) => ({
        id: `board:${b.id}`,
        icon: b.emoji ?? '📋',
        title: b.name,
        subtitle: 'Board',
        to: { name: 'board', params: { boardId: b.id } },
      })),
    },
    { title: 'Cards', items: results.cards.filter((c) => !c.keyMatch).map(card) },
    {
      title: 'Comments',
      items: results.comments.map((m) => ({
        id: `comment:${m.id}`,
        icon: '💬',
        title: m.snippet,
        subtitle: `${m.authorName} on ${m.cardTitle}`,
        hint: m.cardKey,
        to: { name: 'card', params: { boardId: m.boardId, cardId: m.cardId } },
      })),
    },
  ]
  return sections.filter((s) => s.items.length > 0)
}

/** Command palette search: debounced queries, stale responses dropped, recent picks per user. */
export const useSearchStore = defineStore('search', () => {
  const query = ref('')
  const results = ref<SearchResults | null>(null)
  const loading = ref(false)
  const failed = ref(false)
  const recent = ref<SearchItem[]>([])
  const boardId = ref<string | null>(null)
  let userId: string | null = null
  let timer: ReturnType<typeof setTimeout> | undefined
  let request = 0

  const searching = computed(() => query.value.trim().length >= minQueryLength)

  const sections = computed<PaletteSection<SearchItem>[]>(() => {
    const commands = boardCommands(boardId.value, query.value)
    const board = commands.length ? [{ title: 'This board', items: commands }] : []
    if (!searching.value) {
      return [...board, ...(recent.value.length ? [{ title: 'Recent', items: recent.value }] : [])]
    }
    return [...board, ...(results.value ? toSections(results.value) : [])]
  })

  /** The board open behind the palette, for the "This board" section. */
  function setBoard(id: string | null) {
    boardId.value = id
  }

  /** Loads the recent list of the signed-in user. */
  function setUser(id: string) {
    if (userId === id) return
    userId = id
    recent.value = readRecent(id)
  }

  async function run(text: string) {
    const mine = ++request
    try {
      const response = await searchApi.search(text)
      if (mine !== request) return
      results.value = response
      failed.value = false
    } catch {
      if (mine !== request) return
      results.value = null
      failed.value = true
    } finally {
      if (mine === request) loading.value = false
    }
  }

  function setQuery(value: string) {
    query.value = value
    clearTimeout(timer)
    const text = value.trim()
    if (text.length < minQueryLength) {
      // Invalidates any response still in flight.
      request++
      results.value = null
      loading.value = false
      failed.value = false
      return
    }
    loading.value = true
    timer = setTimeout(() => void run(text), debounceMs)
  }

  function reset() {
    setQuery('')
  }

  function remember(item: SearchItem) {
    if (item.id.startsWith('cmd:')) return
    recent.value = [item, ...recent.value.filter((r) => r.id !== item.id)].slice(0, maxRecent)
    if (userId) writeRecent(userId, recent.value)
  }

  return {
    query,
    results,
    loading,
    failed,
    recent,
    searching,
    sections,
    setUser,
    setBoard,
    setQuery,
    reset,
    remember,
  }
})
