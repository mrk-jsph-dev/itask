import { test, expect, type Page } from '@playwright/test'

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

const grace = { id: 'u2', displayName: 'Grace Hopper', avatarColor: '#0F766E' }

const board = {
  id: 'b1',
  name: 'Roadmap',
  emoji: '🚀',
  description: null,
  color: '#0F766E',
  role: 'Editor',
  isStarred: false,
  memberCount: 2,
  archivedAt: null,
  keyPrefix: 'RM',
  rowVersion: 'AQ==',
}

const card = { key: 'RM-1', title: 'Write the spec' }
const minutesAgo = (m: number) => new Date(Date.now() - m * 60_000).toISOString()
const daysAgo = (d: number) => new Date(Date.now() - d * 86_400_000).toISOString()

const entries = [
  {
    id: 'e1',
    type: 'card.moved',
    data: { card, fromList: 'To do', toList: 'Review' },
    createdAt: minutesAgo(1),
    actor: me,
  },
  {
    id: 'e2',
    type: 'comment.added',
    data: { card, commentId: 'm1' },
    createdAt: minutesAgo(2),
    actor: grace,
  },
  {
    id: 'e3',
    type: 'checklist.checked',
    data: { card, item: 'Outline' },
    createdAt: minutesAgo(3),
    actor: me,
  },
  {
    id: 'e4',
    type: 'card.created',
    data: { card, list: 'To do' },
    createdAt: daysAgo(3),
    actor: me,
  },
].map((e) => ({
  ...e,
  cardId: 'c1',
  board: { id: board.id, name: board.name, emoji: board.emoji },
  actor: { id: e.actor.id, displayName: e.actor.displayName, avatarColor: e.actor.avatarColor },
}))

async function mockApi(page: Page) {
  const requests: string[] = []
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const url = new URL(route.request().url())
      const { pathname, searchParams } = url
      const type = searchParams.get('type')
      const actorId = searchParams.get('actorId')
      const feed = (actor?: string) => ({
        items: entries.filter(
          (e) => (!type || e.type.startsWith(`${type}.`)) && (!actor || e.actor.id === actor),
        ),
        nextCursor: null,
      })

      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/me/activity') {
        requests.push(`${pathname}${url.search}`)
        return route.fulfill({ json: feed(me.id) })
      }
      if (pathname === '/api/boards/b1/activity') {
        requests.push(`${pathname}${url.search}`)
        return route.fulfill({ json: feed(actorId ?? undefined) })
      }
      if (pathname === '/api/boards/b1') return route.fulfill({ json: board })
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/lists') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/members') {
        return route.fulfill({
          json: [
            {
              userId: me.id,
              displayName: me.displayName,
              email: me.email,
              avatarColor: me.avatarColor,
              role: 'Editor',
              joinedAt: me.createdAt,
            },
            {
              userId: grace.id,
              displayName: grace.displayName,
              email: 'grace@example.com',
              avatarColor: grace.avatarColor,
              role: 'Editor',
              joinedAt: me.createdAt,
            },
          ],
        })
      }
      return route.fulfill({ status: 404, json: { title: `No mock for ${pathname}` } })
    },
  )
  return requests
}

test('my activity groups by day and filters by type', async ({ page }) => {
  await mockApi(page)
  await page.goto('/activity')

  await expect(page.getByRole('heading', { name: '⚡ Recent activity' })).toBeVisible()
  const today = page.getByRole('region', { name: 'Today' })
  await expect(today.getByRole('listitem')).toHaveCount(2)
  await expect(today.getByText('moved to Review')).toBeVisible()
  await expect(today.getByText('🚀 Roadmap ·').first()).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Today' })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Yesterday' })).toHaveCount(0)
  await expect(page.getByText('created in To do')).toBeVisible()

  await page.getByRole('button', { name: 'Tasks' }).click()
  await expect(page.getByRole('main').getByRole('listitem')).toHaveCount(1)
  await expect(page.getByText('completed the subtask “Outline” on')).toBeVisible()

  await expect(page.getByRole('link', { name: 'Write the spec' })).toHaveAttribute(
    'href',
    '/boards/b1/cards/c1',
  )
})

test('board activity opens from the toolbar and filters by member', async ({ page }) => {
  const requests = await mockApi(page)
  await page.goto('/boards/b1')

  await page.getByRole('link', { name: 'Board activity' }).click()
  await expect(page).toHaveURL(/\/boards\/b1\/activity$/)
  await expect(page.getByRole('heading', { name: '🚀 Roadmap activity' })).toBeVisible()
  await expect(page.getByRole('main').getByRole('listitem')).toHaveCount(4)

  await page.getByLabel('Filter by member').selectOption({ label: 'Grace Hopper' })
  await expect(page.getByRole('main').getByRole('listitem')).toHaveCount(1)
  await expect(page.getByText('commented on')).toBeVisible()
  expect(requests).toContain(`/api/boards/b1/activity?actorId=${grace.id}`)
})
