import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import SkeletonBlock from '../SkeletonBlock.vue'

describe('SkeletonBlock', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('shimmers only after 150 ms', async () => {
    const wrapper = mount(SkeletonBlock)
    expect(wrapper.classes()).not.toContain('skel')

    await vi.advanceTimersByTimeAsync(149)
    expect(wrapper.classes()).not.toContain('skel')

    await vi.advanceTimersByTimeAsync(1)
    expect(wrapper.classes()).toContain('skel')
  })

  it('shows at once with no delay', () => {
    expect(mount(SkeletonBlock, { props: { delay: 0 } }).classes()).toContain('skel')
  })
})
