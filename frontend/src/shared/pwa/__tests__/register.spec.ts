import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'

import UpdatePrompt from '../UpdatePrompt.vue'
import { api } from '@/shared/api/http'
import {
  cacheSessionOnFirstInstall,
  dismissUpdate,
  needRefresh,
  registerServiceWorker,
  updateCheckInterval,
  watchForUpdates,
} from '../register'

const updateSW = vi.fn<(reloadPage?: boolean) => Promise<void>>().mockResolvedValue(undefined)
let options: { onNeedRefresh?: () => void } = {}

vi.mock('virtual:pwa-register', () => ({
  registerSW: (o: typeof options) => {
    options = o
    return updateSW
  },
}))

function setVisibility(state: DocumentVisibilityState) {
  Object.defineProperty(document, 'visibilityState', { value: state, configurable: true })
  document.dispatchEvent(new Event('visibilitychange'))
}

describe('service worker updates', () => {
  beforeEach(() => {
    Object.defineProperty(navigator, 'serviceWorker', {
      value: { controller: {} },
      configurable: true,
    })
    updateSW.mockClear()
  })
  afterEach(() => dismissUpdate())

  it('prompts when a new version waits, and Reload activates it', async () => {
    await registerServiceWorker()
    const wrapper = mount(UpdatePrompt)
    expect(wrapper.text()).toBe('')

    options.onNeedRefresh?.()
    await nextTick()
    expect(wrapper.text()).toContain('A new version of iTask is ready.')

    await wrapper.get('button').trigger('click')

    expect(updateSW).toHaveBeenCalledWith(true)
    expect(needRefresh.value).toBe(false)
  })

  it('Later hides the prompt without reloading', async () => {
    await registerServiceWorker()
    options.onNeedRefresh?.()
    const wrapper = mount(UpdatePrompt)

    await wrapper.get('[aria-label=Later]').trigger('click')

    expect(wrapper.text()).toBe('')
    expect(updateSW).not.toHaveBeenCalled()
  })

  it('checks for a new version on return to the tab at most once an hour', () => {
    let now = 0
    const update = vi.fn<() => Promise<void>>().mockResolvedValue(undefined)
    watchForUpdates({ update } as unknown as ServiceWorkerRegistration, () => now)

    now = updateCheckInterval - 1
    setVisibility('visible')
    expect(update).not.toHaveBeenCalled()

    now = updateCheckInterval
    setVisibility('hidden')
    setVisibility('visible')
    setVisibility('visible')
    expect(update).toHaveBeenCalledOnce()
  })

  it('reads the session again once the first service worker takes over', () => {
    const get = vi.spyOn(api, 'get').mockResolvedValue({})
    const container = new EventTarget() as ServiceWorkerContainer
    Object.defineProperty(container, 'controller', { value: null })

    cacheSessionOnFirstInstall(container)
    container.dispatchEvent(new Event('controllerchange'))
    container.dispatchEvent(new Event('controllerchange'))

    expect(get.mock.calls).toEqual([['/me'], ['/boards']])
  })

  it('does nothing when a worker already controls the page', () => {
    const get = vi.spyOn(api, 'get').mockResolvedValue({})
    const container = new EventTarget() as ServiceWorkerContainer
    Object.defineProperty(container, 'controller', { value: {} })

    cacheSessionOnFirstInstall(container)
    container.dispatchEvent(new Event('controllerchange'))

    expect(get).not.toHaveBeenCalled()
  })
})
