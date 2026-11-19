import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import BoardToolbar from '../components/BoardToolbar.vue'
import { emptyFilter, type BoardState } from '../filters'

const state = (change: Partial<BoardState> = {}): BoardState => ({
  view: 'board',
  filter: emptyFilter(),
  sort: 'position',
  group: 'none',
  month: null,
  ...change,
})

const labels = [{ id: 'bug', name: 'Bug', color: '#B91C1C', emoji: null }]
const members = [{ userId: 'ada', displayName: 'Ada', avatarColor: '#1D5BF5' }]

const mountToolbar = (s: BoardState) =>
  mount(BoardToolbar, { props: { state: s, labels, members }, attachTo: document.body })

describe('BoardToolbar', () => {
  it('switches views from the tabs, also with arrow keys', async () => {
    const wrapper = mountToolbar(state())
    const tabs = wrapper.findAll('[role=tab]')

    expect(tabs.map((t) => t.attributes('aria-selected'))).toEqual(['true', 'false', 'false'])
    await tabs[2]!.trigger('click')
    await tabs[0]!.trigger('keydown', { key: 'ArrowLeft' })

    expect(wrapper.emitted('change')).toEqual([[{ view: 'calendar' }], [{ view: 'calendar' }]])
    wrapper.unmount()
  })

  it('shows Group by on the List view only', () => {
    expect(mountToolbar(state()).text()).not.toContain('Group by')
    expect(mountToolbar(state({ view: 'list' })).text()).toContain('Group by')
  })

  it('toggles filter values and clears them', async () => {
    const wrapper = mountToolbar(state({ filter: { ...emptyFilter(), due: ['overdue'] } }))

    expect(wrapper.find('button[aria-label="Filter, 1 active"]').exists()).toBe(true)
    await wrapper.find('button[aria-haspopup=dialog]').trigger('click')
    const bug = wrapper.findAll('[role=dialog] button').find((b) => b.text().includes('Bug'))!
    await bug.trigger('click')
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Clear filters')!
      .trigger('click')

    const changes = wrapper.emitted('change')!
    expect(changes[0]).toEqual([
      { filter: { ...emptyFilter(), due: ['overdue'], labels: ['bug'] } },
    ])
    expect(changes[1]).toEqual([{ filter: emptyFilter() }])
    wrapper.unmount()
  })

  it('picks a sort from the menu', async () => {
    const wrapper = mountToolbar(state())

    await wrapper.find('button[aria-haspopup=menu]').trigger('click')
    await wrapper
      .findAll('[role=menuitemradio]')
      .find((b) => b.text().includes('Due date'))!
      .trigger('click')

    expect(wrapper.emitted('change')).toEqual([[{ sort: 'due' }]])
    wrapper.unmount()
  })
})
