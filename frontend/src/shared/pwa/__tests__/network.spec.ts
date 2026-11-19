import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'

import { api, ApiError } from '@/shared/api/http'
import OfflineBanner from '../OfflineBanner.vue'
import { OFFLINE_HINT, offlineHeader, online } from '../network'

function goOffline() {
  window.dispatchEvent(new Event('offline'))
}

describe('network state', () => {
  afterEach(() => {
    window.dispatchEvent(new Event('online'))
    vi.unstubAllGlobals()
  })

  it('follows the browser online and offline events', () => {
    expect(online.value).toBe(true)
    goOffline()
    expect(online.value).toBe(false)
    window.dispatchEvent(new Event('online'))
    expect(online.value).toBe(true)
  })

  it('shows the offline banner only while offline', async () => {
    const wrapper = mount(OfflineBanner)
    expect(wrapper.find('[data-testid=offline-banner]').exists()).toBe(false)

    goOffline()
    await nextTick()

    expect(wrapper.get('[role=status]').text()).toContain('read-only')
  })

  it('rejects writes while offline without touching the network', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>()
    vi.stubGlobal('fetch', fetch)
    goOffline()

    const error = await api.post('/boards', { name: 'x' }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(0)
    expect((error as ApiError).message).toBe(OFFLINE_HINT)
    expect(fetch).not.toHaveBeenCalled()
  })

  it('still sends reads while offline, so the service worker can answer from its cache', async () => {
    const fetch = vi
      .fn<typeof globalThis.fetch>()
      .mockResolvedValue(new Response('[]', { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    goOffline()

    await expect(api.get('/boards')).resolves.toEqual([])
    expect(fetch).toHaveBeenCalledOnce()
  })

  it('counts answers from the service worker cache as offline until the network answers', async () => {
    const fetch = vi
      .fn<typeof globalThis.fetch>()
      .mockResolvedValueOnce(new Response('[]', { headers: { [offlineHeader]: '1' } }))
      .mockResolvedValueOnce(new Response('[]'))
    vi.stubGlobal('fetch', fetch)

    await api.get('/boards')
    expect(online.value).toBe(false)

    await api.get('/boards')
    expect(online.value).toBe(true)
  })
})
