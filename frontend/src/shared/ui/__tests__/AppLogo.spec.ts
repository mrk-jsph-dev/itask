import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'

import AppLogo from '../AppLogo.vue'

describe('AppLogo', () => {
  it('renders an accessible logo at the requested size', () => {
    const wrapper = mount(AppLogo, { props: { size: 30 } })

    expect(wrapper.attributes('aria-label')).toBe('iTask logo')
    expect(wrapper.attributes('width')).toBe('30')
  })
})
