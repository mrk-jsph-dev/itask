import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import { useAuthStore } from '@/features/auth/store'
import type { BoardSummary, Member } from '@/features/boards/api'
import { useBoardsStore } from '@/features/boards/store'
import AppSidebar from '../AppSidebar.vue'
import { shellMode, sidebarBoards } from '../sidebar'

const board = (id: string, name: string, isStarred = false): BoardSummary => ({
  id,
  name,
  emoji: '🚀',
  description: null,
  color: '#1D5BF5',
  role: 'Owner',
  isStarred,
  memberCount: 1,
  archivedAt: null,
})

const me = {
  id: 'u1',
  email: 'ada@example.com',
  displayName: 'Ada Lovelace',
  avatarColor: '#1D5BF5',
  timeZone: 'UTC',
  isPlatformAdmin: false,
  isDemo: false,
  createdAt: '2026-11-18T00:00:00Z',
}

const member = (userId: string, displayName: string): Member => ({
  userId,
  displayName,
  email: `${userId}@example.com`,
  avatarColor: '#0F766E',
  role: 'Editor',
  joinedAt: '2026-11-18T00:00:00Z',
})

const Empty = { template: '<div />' }

/** The Home nav pill (the logo links to `/` too). */
const homePill = (wrapper: Awaited<ReturnType<typeof mountAt>>) =>
  wrapper.findAll('a[href="/"]').find((a) => a.text().includes('Home'))!

async function mountAt(path: string, props: { compact?: boolean } = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: Empty },
      { path: '/activity', name: 'activity', component: Empty },
      { path: '/profile', name: 'profile', component: Empty },
      { path: '/admin/users', name: 'admin-users', component: Empty },
      { path: '/boards/:boardId', name: 'board', component: Empty },
      { path: '/boards/:boardId/cards/:cardId', name: 'card', component: Empty },
    ],
  })
  await router.push(path)
  await router.isReady()
  const wrapper = mount(AppSidebar, { props, global: { plugins: [router] } })
  await flushPromises()
  return wrapper
}

describe('sidebar helpers', () => {
  it('picks the shell mode by width', () => {
    expect(shellMode(1440)).toBe('full')
    expect(shellMode(1280)).toBe('full')
    expect(shellMode(1279)).toBe('rail')
    expect(shellMode(768)).toBe('rail')
    expect(shellMode(767)).toBe('sheet')
    expect(shellMode(390)).toBe('sheet')
  })

  it('lists starred boards first, keeps order and caps the list', () => {
    const boards = [board('a', 'A'), board('b', 'B', true), board('c', 'C'), board('d', 'D', true)]

    expect(sidebarBoards(boards).map((b) => b.id)).toEqual(['b', 'd', 'a', 'c'])
    expect(sidebarBoards(boards, 3).map((b) => b.id)).toEqual(['b', 'd', 'a'])
  })
})

describe('AppSidebar', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAuthStore().user = { ...me }
    useBoardsStore().navBoards = [board('b1', 'Roadmap'), board('b2', 'Website', true)]
  })

  it('marks the current page and the open board', async () => {
    const home = await mountAt('/')
    expect(homePill(home).attributes('aria-current')).toBe('page')

    const onBoard = await mountAt('/boards/b1')
    const links = onBoard.findAll('a[href^="/boards/"]')
    expect(links.map((l) => l.text())).toEqual(['🚀Website★Starred', '🚀Roadmap'])
    expect(onBoard.get('a[href="/boards/b1"]').attributes('aria-current')).toBe('page')
    expect(homePill(onBoard).attributes('aria-current')).toBeUndefined()
  })

  it('shows Admin only to platform admins', async () => {
    expect((await mountAt('/')).text()).not.toContain('Admin')

    useAuthStore().user = { ...me, isPlatformAdmin: true }
    expect((await mountAt('/')).find('a[href="/admin/users"]').exists()).toBe(true)
  })

  it('lists board members only on that board', async () => {
    const boards = useBoardsStore()
    boards.current = { ...board('b1', 'Roadmap'), keyPrefix: 'RM', rowVersion: 'AQ==' }
    boards.members = [member('u1', 'Ada Lovelace'), member('u2', 'Alan Turing')]

    const onBoard = await mountAt('/boards/b1/cards/c1')
    expect(onBoard.text()).toContain('Board members')
    expect(onBoard.text()).toContain('You')
    expect(onBoard.text()).toContain('Alan Turing')

    expect((await mountAt('/')).text()).not.toContain('Board members')
    expect((await mountAt('/boards/b2')).text()).not.toContain('Board members')
  })

  it('keeps labels for screen readers in the icon rail', async () => {
    const rail = await mountAt('/', { compact: true })

    const home = homePill(rail)
    expect(home.attributes('title')).toBe('Home')
    expect(home.get('span.sr-only').text()).toBe('Home')
    expect(rail.find('kbd').exists()).toBe(false)
  })

  it('opens search and New board', async () => {
    const wrapper = await mountAt('/')

    await wrapper.get('button[aria-haspopup="dialog"]').trigger('click')
    await wrapper.get('button[aria-label="New board"]').trigger('click')

    expect(wrapper.emitted('search')).toHaveLength(1)
    expect(useBoardsStore().creating).toBe(true)
  })
})
