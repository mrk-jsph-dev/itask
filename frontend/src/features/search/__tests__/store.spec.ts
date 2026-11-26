import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { searchApi, type SearchResults } from '../api'
import {
  boardCommands,
  debounceMs,
  maxRecent,
  toSections,
  useSearchStore,
  type SearchItem,
} from '../store'

const results = (title = 'Write spec'): SearchResults => ({
  boards: [{ id: 'b1', name: 'Roadmap', emoji: '🚀', color: '#1D5BF5' }],
  cards: [
    {
      id: 'c2',
      boardId: 'b1',
      key: 'RM-2',
      title,
      boardName: 'Roadmap',
      boardEmoji: '🚀',
      listName: 'To do',
      completedAt: null,
      keyMatch: false,
    },
    {
      id: 'c1',
      boardId: 'b1',
      key: 'RM-1',
      title: 'First',
      boardName: 'Roadmap',
      boardEmoji: null,
      listName: 'Done',
      completedAt: '2026-11-18T00:00:00Z',
      keyMatch: true,
    },
  ],
  comments: [
    {
      id: 'm1',
      cardId: 'c2',
      boardId: 'b1',
      cardKey: 'RM-2',
      cardTitle: title,
      authorName: 'Ada',
      createdAt: '2026-11-18T00:00:00Z',
      snippet: '…the spec detail…',
    },
  ],
})

const item = (id: string): SearchItem => ({
  id,
  icon: '📋',
  title: id,
  to: { name: 'board', params: { boardId: id } },
})

describe('toSections', () => {
  it('puts key matches first and leads each row to its route', () => {
    const sections = toSections(results())

    expect(sections.map((s) => s.title)).toEqual(['Key match', 'Boards', 'Cards', 'Comments'])
    expect(sections[0]!.items[0]).toMatchObject({ id: 'card:c1', icon: '✅', hint: 'RM-1' })
    expect(sections[3]!.items[0]!.to).toEqual({
      name: 'card',
      params: { boardId: 'b1', cardId: 'c2' },
    })
  })

  it('drops empty sections', () => {
    expect(toSections({ boards: [], cards: [], comments: [] })).toEqual([])
  })
})

describe('search store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
    localStorage.clear()
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('debounces typing into one request', async () => {
    const search = vi.spyOn(searchApi, 'search').mockResolvedValue(results())
    const store = useSearchStore()

    store.setQuery('wr')
    store.setQuery('wri')
    store.setQuery('writ')
    expect(store.loading).toBe(true)
    await vi.advanceTimersByTimeAsync(debounceMs)

    expect(search).toHaveBeenCalledTimes(1)
    expect(search).toHaveBeenCalledWith('writ')
    expect(store.loading).toBe(false)
    expect(store.sections).toHaveLength(4)
  })

  it('does not search below two characters', async () => {
    const search = vi.spyOn(searchApi, 'search').mockResolvedValue(results())
    const store = useSearchStore()

    store.setQuery(' w ')
    await vi.advanceTimersByTimeAsync(debounceMs)

    expect(search).not.toHaveBeenCalled()
    expect(store.loading).toBe(false)
  })

  it('ignores a response that arrives after a newer query', async () => {
    let resolveOld: (value: SearchResults) => void = () => {}
    vi.spyOn(searchApi, 'search')
      .mockReturnValueOnce(new Promise((resolve) => (resolveOld = resolve)))
      .mockResolvedValueOnce(results('New'))
    const store = useSearchStore()

    store.setQuery('old')
    await vi.advanceTimersByTimeAsync(debounceMs)
    store.setQuery('new')
    await vi.advanceTimersByTimeAsync(debounceMs)
    resolveOld(results('Old'))
    await vi.runAllTimersAsync()

    expect(store.results?.cards[0]?.title).toBe('New')
  })

  it('clearing the query drops an in-flight response', async () => {
    let resolve: (value: SearchResults) => void = () => {}
    vi.spyOn(searchApi, 'search').mockReturnValueOnce(new Promise((r) => (resolve = r)))
    const store = useSearchStore()

    store.setQuery('spec')
    await vi.advanceTimersByTimeAsync(debounceMs)
    store.reset()
    resolve(results())
    await vi.runAllTimersAsync()

    expect(store.results).toBeNull()
  })

  it('flags a failed search', async () => {
    vi.spyOn(searchApi, 'search').mockRejectedValue(new Error('429'))
    const store = useSearchStore()

    store.setQuery('spec')
    await vi.advanceTimersByTimeAsync(debounceMs)

    expect(store.failed).toBe(true)
    expect(store.loading).toBe(false)
  })

  it('keeps recent picks per user, newest first, without duplicates', () => {
    const store = useSearchStore()
    store.setUser('u1')
    for (let i = 0; i < maxRecent + 2; i++) store.remember(item(`b${i}`))
    store.remember(item('b5'))

    expect(store.recent).toHaveLength(maxRecent)
    expect(store.recent[0]!.id).toBe('b5')
    expect(store.recent.filter((r) => r.id === 'b5')).toHaveLength(1)
    expect(store.sections).toEqual([{ title: 'Recent', items: store.recent }])

    setActivePinia(createPinia())
    const other = useSearchStore()
    other.setUser('u2')
    expect(other.recent).toEqual([])
    other.setUser('u1')
    expect(other.recent[0]!.id).toBe('b5')
  })

  it('treats unreadable stored recents as empty', () => {
    localStorage.setItem('itask.search.recent.u1', '{not json')
    const store = useSearchStore()

    store.setUser('u1')

    expect(store.recent).toEqual([])
  })

  it('offers board commands on a board page and filters them by the query', () => {
    expect(boardCommands(null, '')).toEqual([])
    expect(boardCommands('b1', '').map((c) => c.title)).toEqual(['Insights', 'Activity'])
    expect(boardCommands('b1', 'ins').map((c) => c.to)).toEqual([
      { name: 'board-insights', params: { boardId: 'b1' } },
    ])

    const store = useSearchStore()
    store.setBoard('b1')
    expect(store.sections[0]?.title).toBe('This board')
    store.remember(store.sections[0]!.items[0]!)
    expect(store.recent).toEqual([])
  })
})
