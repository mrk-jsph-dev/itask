import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/shared/api/http'
import { toasts } from '@/shared/ui/toast'
import { boardsApi, templatesApi, type BoardDetail, type BoardSummary } from '../api'
import { useBoardsStore } from '../store'

const summary = (name: string, changes: Partial<BoardSummary> = {}): BoardSummary => ({
  id: name.toLowerCase(),
  name,
  emoji: null,
  description: null,
  color: '#1D5BF5',
  role: 'Owner',
  isStarred: false,
  memberCount: 1,
  archivedAt: null,
  ...changes,
})

describe('boards store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('loads active or archived boards', async () => {
    const mine = vi.spyOn(boardsApi, 'mine').mockResolvedValue([summary('Alpha')])
    const store = useBoardsStore()

    await store.load(true)

    expect(mine).toHaveBeenCalledWith({ archived: true })
    expect(store.showArchived).toBe(true)
    expect(store.boards).toHaveLength(1)
  })

  it('inserts a created board in name order', async () => {
    vi.spyOn(boardsApi, 'mine').mockResolvedValue([summary('Alpha'), summary('Gamma')])
    vi.spyOn(boardsApi, 'create').mockResolvedValue({
      id: 'beta',
      name: 'Beta',
      emoji: '🚀',
      description: null,
      color: '#0F766E',
      role: 'Owner',
    })
    const store = useBoardsStore()
    await store.load(false)

    await store.create({ name: 'Beta', emoji: '🚀', description: null, color: '#0F766E' })

    expect(store.boards!.map((b) => b.name)).toEqual(['Alpha', 'Beta', 'Gamma'])
  })

  it('creates from a template through the templates API', async () => {
    const fromTemplate = vi.spyOn(templatesApi, 'createBoard').mockResolvedValue({
      id: 'sprint',
      name: 'Sprint 1',
      emoji: '🏃',
      description: null,
      color: '#7C3AED',
      role: 'Owner',
    })
    const blank = vi.spyOn(boardsApi, 'create')
    const store = useBoardsStore()
    const input = { name: 'Sprint 1', emoji: '🏃', description: null, color: '#7C3AED' }

    const created = await store.create(input, 'sprint')

    expect(fromTemplate).toHaveBeenCalledWith('sprint', input)
    expect(blank).not.toHaveBeenCalled()
    expect(created.id).toBe('sprint')
  })

  it('stars optimistically and lists the board as starred', async () => {
    vi.spyOn(boardsApi, 'mine').mockResolvedValue([summary('Alpha')])
    const star = vi.spyOn(boardsApi, 'star').mockResolvedValue()
    const store = useBoardsStore()
    await store.load(false)

    await store.toggleStar('alpha', false)

    expect(star).toHaveBeenCalledWith('alpha')
    expect(store.starred.map((b) => b.id)).toEqual(['alpha'])
  })

  it('rolls the star back and shows a toast when the request fails', async () => {
    vi.spyOn(boardsApi, 'mine').mockResolvedValue([summary('Alpha', { isStarred: true })])
    vi.spyOn(boardsApi, 'unstar').mockRejectedValue(new ApiError(500, 'Boom'))
    const store = useBoardsStore()
    await store.load(false)

    await store.toggleStar('alpha', true)

    expect(store.boards![0]!.isStarred).toBe(true)
    expect(toasts.value[toasts.value.length - 1]).toMatchObject({
      tone: 'error',
      text: "Couldn't update the star.",
    })
  })

  it('drops archived boards from the active list and marks the open board', async () => {
    vi.spyOn(boardsApi, 'mine').mockResolvedValue([summary('Alpha'), summary('Beta')])
    vi.spyOn(boardsApi, 'archive').mockResolvedValue()
    vi.spyOn(boardsApi, 'get').mockResolvedValue({
      ...summary('Alpha'),
      keyPrefix: 'ALP',
      rowVersion: 'AQ==',
    })
    vi.spyOn(boardsApi, 'members').mockResolvedValue([])
    vi.spyOn(boardsApi, 'invites').mockResolvedValue([])
    const store = useBoardsStore()
    await store.load(false)
    await store.open('alpha')

    await store.archive('alpha')

    expect(store.boards!.map((b) => b.id)).toEqual(['beta'])
    expect(store.current?.archivedAt).not.toBeNull()
  })

  it('saves details with the row version and keeps the new one', async () => {
    const board: BoardDetail = { ...summary('Alpha'), keyPrefix: 'ALP', rowVersion: 'v1' }
    vi.spyOn(boardsApi, 'get').mockResolvedValue(board)
    vi.spyOn(boardsApi, 'members').mockResolvedValue([])
    vi.spyOn(boardsApi, 'invites').mockResolvedValue([])
    const update = vi.spyOn(boardsApi, 'update').mockResolvedValue({
      id: 'alpha',
      name: 'Renamed',
      emoji: null,
      description: 'Text',
      color: '#1D5BF5',
      keyPrefix: 'ALP',
      rowVersion: 'v2',
    })
    const store = useBoardsStore()
    await store.open('alpha')

    const input = { name: 'Renamed', emoji: null, description: 'Text', color: '#1D5BF5' }
    await store.saveDetails(input)

    expect(update).toHaveBeenCalledWith('alpha', input, 'v1')
    expect(store.current).toMatchObject({ name: 'Renamed', rowVersion: 'v2' })
  })

  it('loads invites only for owners', async () => {
    vi.spyOn(boardsApi, 'get').mockResolvedValue({
      ...summary('Alpha', { role: 'Viewer' }),
      keyPrefix: 'ALP',
      rowVersion: 'v',
    })
    vi.spyOn(boardsApi, 'members').mockResolvedValue([])
    const invites = vi.spyOn(boardsApi, 'invites').mockResolvedValue([])
    const store = useBoardsStore()

    await store.open('alpha')

    expect(invites).not.toHaveBeenCalled()
    expect(store.isOwner).toBe(false)
  })

  it('adds a created invite to the pending list without its token', async () => {
    vi.spyOn(boardsApi, 'get').mockResolvedValue({
      ...summary('Alpha'),
      keyPrefix: 'ALP',
      rowVersion: 'v',
    })
    vi.spyOn(boardsApi, 'members').mockResolvedValue([])
    vi.spyOn(boardsApi, 'invites').mockResolvedValue([])
    vi.spyOn(boardsApi, 'createInvite').mockResolvedValue({
      id: 'i1',
      token: 'secret',
      email: null,
      role: 'Editor',
      expiresAt: '2026-11-25T00:00:00Z',
    })
    const store = useBoardsStore()
    await store.open('alpha')

    const created = await store.createInvite('Editor', null)

    expect(created.token).toBe('secret')
    expect(store.invites).toHaveLength(1)
    expect(store.invites[0]).not.toHaveProperty('token')
  })
})
