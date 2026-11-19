import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'

import CommandPalette from '../CommandPalette.vue'
import type { PaletteSection } from '../commandPalette'

const sections: PaletteSection[] = [
  { title: 'Boards', items: [{ id: 'b1', icon: '🚀', title: 'Roadmap' }] },
  {
    title: 'Cards',
    items: [
      { id: 'c1', icon: '🗂️', title: 'Write spec', hint: 'RM-1' },
      { id: 'c2', icon: '🗂️', title: 'Review spec', hint: 'RM-2' },
    ],
  },
]

function mountPalette(
  props: Partial<{ sections: PaletteSection[]; loading: boolean; emptyText: string }> = {},
) {
  return mount(CommandPalette, {
    props: { sections, label: 'Search', query: '', ...props },
    attachTo: document.body,
  })
}

const options = () => [...document.body.querySelectorAll('[role="option"]')]
const input = () => document.body.querySelector<HTMLInputElement>('input[role="combobox"]')!

describe('CommandPalette', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('moves through rows across sections with the arrow keys and wraps', async () => {
    const wrapper = mountPalette()
    const press = async (key: string) => {
      input().dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }))
      await wrapper.vm.$nextTick()
    }
    const selected = () => options().findIndex((o) => o.getAttribute('aria-selected') === 'true')

    expect(selected()).toBe(0)
    await press('ArrowDown')
    await press('ArrowDown')
    expect(selected()).toBe(2)
    expect(input().getAttribute('aria-activedescendant')).toBe(options()[2]!.id)
    await press('ArrowDown')
    expect(selected()).toBe(0)
    await press('ArrowUp')
    expect(selected()).toBe(2)
    wrapper.unmount()
  })

  it('selects the active row with Enter and closes with Escape', async () => {
    const wrapper = mountPalette()

    input().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
    await wrapper.vm.$nextTick()
    input().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    input().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))

    expect(wrapper.emitted('select')?.[0]).toEqual([sections[1]!.items[0]])
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })

  it('shows skeletons while loading and the empty text otherwise', async () => {
    const wrapper = mountPalette({ sections: [], loading: true, emptyText: 'Nothing here' })
    expect(document.body.querySelectorAll('[data-testid="skeleton"]').length).toBeGreaterThan(0)

    await wrapper.setProps({ loading: false })
    expect(document.body.textContent).toContain('Nothing here')
    wrapper.unmount()
  })

  it('focuses the input on open', async () => {
    const wrapper = mountPalette()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()

    expect(document.activeElement).toBe(input())
    wrapper.unmount()
  })
})
