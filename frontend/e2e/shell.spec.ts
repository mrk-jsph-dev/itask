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

const board = {
  id: 'b1',
  name: 'Roadmap',
  emoji: '🚀',
  description: null,
  color: '#1D5BF5',
  role: 'Owner',
  isStarred: true,
  memberCount: 1,
  archivedAt: null,
}

const card = {
  id: 'c1',
  listId: 'l1',
  number: 1,
  key: 'RM-1',
  title: 'Drag and drop',
  emoji: '🖱️',
  position: 1024,
  dueAt: null as string | null,
  startAt: null,
  priority: 'High',
  coverColor: null,
  completedAt: null as string | null,
  archivedAt: null,
  labels: [],
  assignees: [],
  checklistDone: 1,
  checklistTotal: 3,
  commentCount: 0,
  attachmentCount: 2,
  recurring: false,
  rowVersion: 'v1',
}

/** A signed-out visitor with one starred board holding one card. */
async function mockApi(page: Page, signedIn = false) {
  const state = { signedIn, card: { ...card, dueAt: new Date(Date.now() + 864e5).toISOString() } }
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const { pathname } = new URL(request.url())
      const method = request.method()

      if (pathname === '/api/me')
        return state.signedIn ? route.fulfill({ json: me }) : route.fulfill({ status: 401 })
      if (pathname === '/api/auth/login') {
        state.signedIn = true
        return route.fulfill({ status: 200, body: '' })
      }
      if (pathname === '/api/auth/demo') return route.fulfill({ json: { enabled: false } })
      if (pathname === '/api/me/due-soon') {
        const { id, title, emoji, priority, dueAt } = state.card
        return route.fulfill({
          json: {
            dueThisWeek: 1,
            overdue: 0,
            items: [
              {
                id,
                boardId: 'b1',
                boardName: 'Roadmap',
                boardEmoji: '🚀',
                title,
                emoji,
                priority,
                dueAt,
              },
            ],
          },
        })
      }
      if (pathname === '/api/boards') return route.fulfill({ json: [board] })
      if (pathname === '/api/boards/b1')
        return route.fulfill({ json: { ...board, keyPrefix: 'RM', rowVersion: 'AQ==' } })
      if (pathname === '/api/boards/b1/members') {
        return route.fulfill({
          json: [
            {
              userId: 'u1',
              displayName: me.displayName,
              email: me.email,
              avatarColor: me.avatarColor,
              role: 'Owner',
              joinedAt: me.createdAt,
            },
          ],
        })
      }
      if (pathname === '/api/boards/b1/invites' || pathname === '/api/boards/b1/labels')
        return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/lists') {
        return route.fulfill({
          json: [
            {
              id: 'l1',
              name: 'Doing',
              emoji: '🚧',
              position: 1024,
              wipLimit: null,
              archivedAt: null,
              cards: [state.card],
            },
            {
              id: 'l2',
              name: 'Done',
              emoji: '✅',
              position: 2048,
              wipLimit: null,
              archivedAt: null,
              cards: [],
            },
          ],
        })
      }
      if (pathname === '/api/boards/b1/cards/c1/complete' && method === 'POST') {
        state.card = { ...state.card, completedAt: new Date().toISOString() }
        return route.fulfill({ json: state.card })
      }
      if (pathname === '/api/boards/b1/cards/c1') {
        return route.fulfill({
          json: {
            card: state.card,
            description: null,
            createdById: 'u1',
            createdByName: me.displayName,
            createdAt: me.createdAt,
            checklist: [],
            attachments: [],
            recurrence: null,
          },
        })
      }
      if (pathname.endsWith('/comments'))
        return route.fulfill({ json: { items: [], nextCursor: null } })
      if (pathname.endsWith('/activity'))
        return route.fulfill({ json: { items: [], nextCursor: null } })
      return route.fulfill({ status: 404 })
    },
  )
}

test('sign in, open a board from the sidebar, open a card and mark it complete', async ({
  page,
}) => {
  await mockApi(page)

  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()
  await page.getByLabel('Email').fill(me.email)
  await page.getByLabel('Password').fill('correct horse battery')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByText('1 task is due this week.')).toBeVisible()
  await expect(page.getByRole('link', { name: /Drag and drop/ })).toContainText('Tomorrow')

  const sidebar = page.getByRole('navigation', { name: 'Main' })
  await sidebar.getByRole('link', { name: /Roadmap/ }).click()
  await expect(page).toHaveURL(/\/boards\/b1$/)
  await expect(page.getByRole('heading', { name: 'Roadmap', level: 1 })).toBeVisible()
  await expect(sidebar.getByRole('link', { name: /Roadmap/ })).toHaveAttribute(
    'aria-current',
    'page',
  )
  await expect(sidebar.getByText('Board members')).toBeVisible()

  const tile = page.getByRole('article', { name: 'RM-1 Drag and drop' })
  await expect(tile).toContainText('🖱️')
  await expect(tile.getByLabel('Subtasks 1 of 3 done')).toBeVisible()
  await expect(tile.getByLabel('2 files')).toBeVisible()
  await tile.locator('.card-open').click()

  const sheet = page.getByRole('dialog', { name: 'RM-1 Drag and drop' })
  await expect(sheet).toContainText('Roadmap / Doing')
  await sheet.getByRole('button', { name: 'Mark complete' }).click()
  await expect(sheet.getByRole('button', { name: 'Completed' })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(tile).toContainText('✓')
})

test.describe('phone width', () => {
  test.use({ viewport: { width: 390, height: 844 } })

  test('Home and Board never scroll sideways; the menu opens the sidebar', async ({ page }) => {
    await mockApi(page, true)
    const noSideScroll = () =>
      page.evaluate(
        () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
      )

    await page.goto('/')
    await expect(
      page.getByRole('heading', { name: /Good (morning|afternoon|evening)/ }),
    ).toBeVisible()
    expect(await noSideScroll()).toBe(true)

    await page.getByRole('button', { name: 'Open menu' }).click()
    await page
      .getByRole('dialog', { name: 'Menu' })
      .getByRole('link', { name: /Roadmap/ })
      .click()
    await expect(page.getByRole('dialog', { name: 'Menu' })).toBeHidden()
    await expect(page.getByRole('article', { name: 'RM-1 Drag and drop' })).toBeVisible()
    expect(await noSideScroll()).toBe(true)
  })
})
