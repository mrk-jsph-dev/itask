import { test, expect, type Page } from '@playwright/test'

const me = {
  id: 'u1',
  email: 'ada@example.com',
  displayName: 'Ada Lovelace',
  avatarColor: '#1D5BF5',
  timeZone: 'UTC',
  isPlatformAdmin: false,
  isDemo: false,
  hasPassword: true,
  createdAt: '2026-11-18T00:00:00Z',
}

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

const card = {
  id: 'c1',
  listId: 'l1',
  number: 1,
  key: 'RM-1',
  title: 'Write the spec',
  position: 1024,
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
  commentCount: 1,
  emoji: null,
  attachmentCount: 0,
  recurring: false,
  rowVersion: 'v1',
}

/** Mocks one mention on a board with one card, plus the digest preferences. */
async function mockApi(page: Page) {
  const notifications = [
    {
      id: 'n1',
      type: 'mentioned',
      data: { card: { key: 'RM-1', title: 'Write the spec' }, commentId: 'm1' },
      createdAt: new Date(Date.now() - 60_000).toISOString(),
      readAt: null as string | null,
      cardId: 'c1',
      board: { id: 'b1', name: 'Roadmap', emoji: '🚀' },
      actor: { id: 'u2', displayName: 'Grace Hopper', avatarColor: '#0F766E' },
    },
  ]
  let prefs = {
    digest: 'Off',
    types: {
      mentioned: true,
      assigned: true,
      dueSoon: true,
      overdue: true,
      invited: true,
      commentOnMyCard: true,
    },
    emailConfirmed: true,
  }
  const saved: unknown[] = []

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const { pathname } = new URL(request.url())
      const method = request.method()
      const unread = () => notifications.filter((n) => !n.readAt).length

      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards') return route.fulfill({ json: [board] })
      if (pathname === '/api/notifications/unread-count') {
        return route.fulfill({ json: { count: unread() } })
      }
      if (pathname === '/api/notifications') {
        return route.fulfill({ json: { items: notifications, nextCursor: null } })
      }
      const read = pathname.match(/^\/api\/notifications\/(\w+)\/read$/)
      if (read && method === 'POST') {
        notifications.find((n) => n.id === read[1])!.readAt = new Date().toISOString()
        return route.fulfill({ status: 204 })
      }
      if (pathname === '/api/me/notification-preferences') {
        if (method === 'PUT') {
          const body = request.postDataJSON() as typeof prefs
          saved.push(body)
          prefs = { ...prefs, ...body }
        }
        return route.fulfill({ json: prefs })
      }
      if (pathname === '/api/me/stats')
        return route.fulfill({ json: { boards: 1, tasks: 0, done: 0 } })
      if (pathname === '/api/me/due-soon') {
        return route.fulfill({ json: { dueThisWeek: 0, overdue: 0, items: [] } })
      }
      if (pathname === '/api/boards/b1') return route.fulfill({ json: board })
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/members') {
        return route.fulfill({
          json: [{ ...me, userId: me.id, role: 'Editor', joinedAt: me.createdAt }],
        })
      }
      if (pathname === '/api/boards/b1/lists') {
        return route.fulfill({
          json: [
            {
              id: 'l1',
              name: 'To do',
              emoji: '📝',
              position: 1024,
              wipLimit: null,
              archivedAt: null,
              cards: [card],
            },
          ],
        })
      }
      if (pathname === '/api/boards/b1/cards/c1') {
        return route.fulfill({
          json: {
            card,
            description: null,
            createdById: me.id,
            createdByName: me.displayName,
            createdAt: me.createdAt,
            checklist: [],
            attachments: [],
            recurrence: null,
          },
        })
      }
      if (pathname.endsWith('/comments') || pathname.endsWith('/activity')) {
        return route.fulfill({ json: { items: [], nextCursor: null } })
      }
      return route.fulfill({ status: 404, json: { title: 'Not found' } })
    },
  )
  return { saved }
}

test('a mention shows on the bell, opens the card and clears the badge', async ({ page }) => {
  await mockApi(page)
  await page.goto('/')

  const bell = page.getByRole('button', { name: 'Notifications, 1 unread' })
  await expect(bell).toBeVisible()
  await bell.click()

  const panel = page.getByRole('dialog', { name: 'Notifications' })
  await expect(panel).toContainText('Grace Hopper mentioned you on Write the spec')
  await panel.getByRole('link', { name: 'Write the spec' }).click()

  await expect(page).toHaveURL(/\/boards\/b1\/cards\/c1$/)
  await expect(page.getByRole('button', { name: 'Notifications', exact: true })).toBeVisible()
  await expect(page.getByTestId('bell-badge')).toHaveCount(0)
})

test('the digest is turned on from the profile', async ({ page }) => {
  const { saved } = await mockApi(page)
  await page.goto('/profile')

  const form = page.getByRole('form', { name: '🔔 Notifications' })
  await expect(form.getByRole('checkbox', { name: 'Mentions' })).toBeDisabled()
  await form.getByText('Weekly', { exact: true }).click()
  await form.getByRole('checkbox', { name: 'Overdue' }).uncheck()
  await form.getByRole('button', { name: 'Save notification settings' }).click()

  await expect(form.getByRole('status')).toHaveText('Notification settings saved.')
  expect(saved).toEqual([
    expect.objectContaining({
      digest: 'Weekly',
      types: expect.objectContaining({ overdue: false }),
    }),
  ])
})

test('the notifications page groups by day and the sidebar shows the count', async ({ page }) => {
  await mockApi(page)
  await page.goto('/notifications')

  await expect(page.getByRole('heading', { name: 'Notifications', level: 1 })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Today' })).toContainText('mentioned you on')
  await expect(page.getByRole('link', { name: /Notifications/ })).toContainText('1')
})
