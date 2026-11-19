import { afterEach, describe, expect, it, vi } from 'vitest'

import { apiCacheName, clearApiCache } from '../cache'

describe('clearApiCache', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('deletes the API runtime cache', async () => {
    const remove = vi.fn<(name: string) => Promise<boolean>>().mockResolvedValue(true)
    vi.stubGlobal('caches', { delete: remove })

    await clearApiCache()

    expect(remove).toHaveBeenCalledWith(apiCacheName)
  })

  it('does nothing when Cache Storage is missing or fails', async () => {
    await expect(clearApiCache()).resolves.toBeUndefined()

    vi.stubGlobal('caches', {
      delete: vi.fn<(name: string) => Promise<boolean>>().mockRejectedValue(new Error('denied')),
    })
    await expect(clearApiCache()).resolves.toBeUndefined()
  })
})
