import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import { notificationsApi, type NotificationPreferences } from '../api'
import DigestPreferences from '../components/DigestPreferences.vue'
import NotificationBell from '../components/NotificationBell.vue'
import { useNotificationsStore } from '../store'
import { notification } from './fixtures'

const router = () =>
  createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/notifications', component: { template: '<div />' } },
      { path: '/boards/:boardId', name: 'board', component: { template: '<div />' } },
      { path: '/boards/:boardId/cards/:cardId', name: 'card', component: { template: '<div />' } },
    ],
  })

describe('NotificationBell', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.restoreAllMocks())

  it('shows the unread badge and replays it when the count rises', async () => {
    const store = useNotificationsStore()
    const wrapper = mount(NotificationBell, { global: { plugins: [router()] } })
    expect(wrapper.find('[data-testid=bell-badge]').exists()).toBe(false)

    store.unreadCount = 2
    await flushPromises()
    const first = wrapper.find('[data-testid=bell-badge]')
    expect(first.text()).toBe('2')
    expect(first.classes()).toContain('pop')
    expect(wrapper.find('button').attributes('aria-label')).toBe('Notifications, 2 unread')

    store.unreadCount = 12
    await flushPromises()
    expect(wrapper.find('[data-testid=bell-badge]').text()).toBe('9+')
    expect(wrapper.find('[data-testid=bell-badge]').element).not.toBe(first.element)
  })

  it('opens the latest notifications and marks one read on click', async () => {
    vi.spyOn(notificationsApi, 'list').mockResolvedValue({
      items: [notification()],
      nextCursor: null,
    })
    vi.spyOn(notificationsApi, 'unreadCount').mockResolvedValue({ count: 1 })
    const read = vi.spyOn(notificationsApi, 'markRead').mockResolvedValue()
    const wrapper = mount(NotificationBell, {
      global: { plugins: [router()] },
      attachTo: document.body,
    })

    await wrapper.find('button').trigger('click')
    await flushPromises()
    expect(wrapper.find('[role=dialog]').text()).toContain('Ada mentioned you on Fix login')

    await wrapper.find('[role=dialog] a[href="/boards/b1/cards/card1"]').trigger('click')
    await flushPromises()
    expect(read).toHaveBeenCalledWith('n1')
    expect(wrapper.find('[role=dialog]').exists()).toBe(false)
    wrapper.unmount()
  })
})

describe('DigestPreferences', () => {
  afterEach(() => vi.restoreAllMocks())

  const prefs = (change: Partial<NotificationPreferences> = {}): NotificationPreferences => ({
    digest: 'Off',
    types: {
      mentioned: true,
      assigned: true,
      dueSoon: true,
      overdue: true,
      invited: true,
      commentOnMyCard: true,
      unblocked: true,
    },
    emailConfirmed: true,
    ...change,
  })

  it('disables the type toggles while the digest is off and saves the choice', async () => {
    vi.spyOn(notificationsApi, 'preferences').mockResolvedValue(prefs())
    const save = vi
      .spyOn(notificationsApi, 'savePreferences')
      .mockImplementation(async (p) => ({ ...p, emailConfirmed: true }))
    const wrapper = mount(DigestPreferences)
    await flushPromises()

    expect(wrapper.findAll('fieldset')[1]!.attributes('disabled')).toBeDefined()

    await wrapper.find('input[value=Daily]').setValue(true)
    await wrapper.find('input[type=checkbox]').setValue(false)
    expect(wrapper.findAll('fieldset')[1]!.attributes('disabled')).toBeUndefined()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(save).toHaveBeenCalledWith({
      digest: 'Daily',
      types: expect.objectContaining({ mentioned: false, assigned: true }),
    })
    expect(wrapper.text()).toContain('Notification settings saved.')
  })

  it('warns when digests are on but the email is not confirmed', async () => {
    vi.spyOn(notificationsApi, 'preferences').mockResolvedValue(
      prefs({ digest: 'Weekly', emailConfirmed: false }),
    )
    const wrapper = mount(DigestPreferences)
    await flushPromises()

    expect(wrapper.text()).toContain('Confirm your email address')
  })
})
