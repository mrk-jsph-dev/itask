import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/shared/api/http'
import { runToastAction, toasts } from '@/shared/ui/toast'
import { listsApi, type BoardList } from '../api'
import { useListsStore } from '../listsStore'

const list = (name: string, position: number): BoardList => ({
  id: name.toLowerCase(),
  name,
  emoji: null,
  position,
  wipLimit: null,
  archivedAt: null,
})

const lastToast = () => toasts.value[toasts.value.length - 1]

const names = (store: ReturnType<typeof useListsStore>) => store.lists?.map((l) => l.name)

async function loaded() {
  vi.spyOn(listsApi, 'all').mockResolvedValue([list('Todo', 1), list('Doing', 2), list('Done', 3)])
  const store = useListsStore()
  await store.load('b1')
  return store
}

describe('lists store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('moves at once and sends the new neighbours', async () => {
    const move = vi.spyOn(listsApi, 'move').mockResolvedValue({ id: 'done', position: 1.5 })
    const store = await loaded()

    await store.move(2, 1)

    expect(names(store)).toEqual(['Todo', 'Done', 'Doing'])
    expect(move).toHaveBeenCalledWith('b1', 'done', { beforeId: 'todo', afterId: 'doing' })
  })

  it('rolls a failed move back and shows a toast', async () => {
    vi.spyOn(listsApi, 'move').mockRejectedValue(new ApiError(409, 'Lists changed.'))
    const store = await loaded()

    await store.move(0, 2)

    expect(names(store)).toEqual(['Todo', 'Doing', 'Done'])
    expect(lastToast()).toMatchObject({ text: 'Lists changed.', tone: 'error' })
  })

  it('ignores moves past the ends', async () => {
    const move = vi.spyOn(listsApi, 'move')
    const store = await loaded()

    await store.move(0, -1)
    await store.move(2, 3)

    expect(move).not.toHaveBeenCalled()
  })

  it('appends a created list', async () => {
    vi.spyOn(listsApi, 'create').mockResolvedValue({
      id: 'ideas',
      name: 'Ideas',
      emoji: '💡',
      position: 4,
      wipLimit: null,
    })
    const store = await loaded()

    await store.create({ name: 'Ideas', emoji: '💡', wipLimit: null })

    expect(names(store)).toEqual(['Todo', 'Doing', 'Done', 'Ideas'])
  })

  it('archives with an Undo that restores the list', async () => {
    vi.spyOn(listsApi, 'archive').mockResolvedValue(undefined)
    const restore = vi.spyOn(listsApi, 'restore').mockResolvedValue(undefined)
    const store = await loaded()

    await store.archive('doing')
    expect(names(store)).toEqual(['Todo', 'Done'])

    const undo = lastToast()!
    expect(undo.action?.label).toBe('Undo')
    vi.mocked(listsApi.all).mockResolvedValue([list('Todo', 1), list('Done', 3), list('Doing', 4)])
    runToastAction(undo.id)
    await vi.waitFor(() => expect(names(store)).toEqual(['Todo', 'Done', 'Doing']))
    expect(restore).toHaveBeenCalledWith('b1', 'doing')
  })

  it('puts the list back when archiving fails', async () => {
    vi.spyOn(listsApi, 'archive').mockRejectedValue(new ApiError(403, 'Not allowed.'))
    const store = await loaded()

    await store.archive('doing')

    expect(names(store)).toEqual(['Todo', 'Doing', 'Done'])
  })
})
