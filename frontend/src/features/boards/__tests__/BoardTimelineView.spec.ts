import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import type { Card } from '../api'
import BoardTimelineView from '../components/BoardTimelineView.vue'

const card = (id: string, extra: Partial<Card> = {}): Card => ({
  id,
  listId: 'l1',
  number: 1,
  key: `RM-${id}`,
  title: `Task ${id}`,
  emoji: null,
  position: 1,
  dueAt: null,
  startAt: null,
  priority: 'None',
  coverColor: null,
  completedAt: null,
  archivedAt: null,
  labels: [],
  assignees: [],
  checklistDone: 0,
  checklistTotal: 0,
  commentCount: 0,
  attachmentCount: 0,
  recurring: false,
  blocked: false,
  blockerCount: 0,
  blockingCount: 0,
  rowVersion: 'rv',
  ...extra,
})

const at = (d: number, h = 12) => new Date(2026, 2, d, h).toISOString()

function setWidth(phone: boolean) {
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({
      matches: phone,
      addEventListener: vi.fn<() => void>(),
      removeEventListener: vi.fn<() => void>(),
    })),
  )
}

const mountView = (props: Partial<InstanceType<typeof BoardTimelineView>['$props']> = {}) =>
  mount(BoardTimelineView, {
    props: {
      cards: [
        card('a', { startAt: at(2), dueAt: at(4) }),
        card('b', { dueAt: at(10), listId: 'l2' }),
      ],
      undated: [card('c')],
      dependencies: [{ cardId: 'b', blockedById: 'a' }],
      zoom: 'month',
      from: '2026-03-01',
      group: 'none',
      editable: true,
      lists: [
        { id: 'l1', name: 'To do', emoji: null },
        { id: 'l2', name: 'Doing', emoji: null },
      ],
      labels: [],
      members: [],
      ...props,
    },
    attachTo: document.body,
  })

afterEach(() => {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})

describe('BoardTimelineView', () => {
  it('reports its window, groups rows by list and lists undated tasks', () => {
    setWidth(false)
    const wrapper = mountView()

    const [range] = wrapper.emitted('range')![0]!
    expect(range).toMatchObject({ kind: 'window' })
    expect(wrapper.findAll('h3').map((h) => h.text())).toEqual(['To do1', 'Doing1'])
    expect(wrapper.find('[data-bar=a]').classes()).toContain('bar')
    expect(wrapper.find('[data-bar=b]').classes()).toContain('dot')
    expect(wrapper.find('[data-testid=no-dates]').text()).toContain('Task c')
  })

  it('moves a bar with Alt+arrows and resizes with Alt+Shift', async () => {
    setWidth(false)
    const wrapper = mountView()
    const bar = wrapper.find('[data-bar=a]')

    await bar.trigger('keydown', { key: 'ArrowRight', altKey: true })
    await bar.trigger('keydown', { key: 'ArrowLeft', altKey: true, shiftKey: true })
    await wrapper
      .find('[data-handle=start-a]')
      .trigger('keydown', { key: 'ArrowLeft', altKey: true })

    expect(wrapper.emitted('update')).toEqual([
      ['a', { startAt: at(3), dueAt: at(5) }],
      ['a', { dueAt: at(3) }],
      ['a', { startAt: at(1) }],
    ])
  })

  it('opens a card on click and toggles blocker arrows', async () => {
    setWidth(false)
    localStorage.removeItem('itask.timeline.arrows')
    const wrapper = mountView()

    await wrapper.find('[data-bar=b]').trigger('click')
    expect(wrapper.emitted('open')).toEqual([['b']])
    await wrapper.find('[role=switch]').trigger('click')
    expect(localStorage.getItem('itask.timeline.arrows')).toBe('off')
  })

  it('gives viewers no handles and no keyboard edits', async () => {
    setWidth(false)
    const wrapper = mountView({ editable: false })

    expect(wrapper.find('[data-handle]').exists()).toBe(false)
    await wrapper.find('[data-bar=a]').trigger('keydown', { key: 'ArrowRight', altKey: true })
    expect(wrapper.emitted('update')).toBeUndefined()
  })

  it('shows an agenda on phones that opens the card on tap', async () => {
    setWidth(true)
    const wrapper = mountView()
    await wrapper.vm.$nextTick()

    const agenda = wrapper.find('[data-testid=timeline-agenda]')
    expect(agenda.exists()).toBe(true)
    expect(wrapper.find('[data-handle]').exists()).toBe(false)
    await agenda.findAll('button')[0]!.trigger('click')
    expect(wrapper.emitted('open')).toEqual([['a']])
  })
})
