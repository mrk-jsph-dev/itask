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
  color: '#0F766E',
  role: 'Editor',
  isStarred: false,
  memberCount: 1,
  archivedAt: null,
  keyPrefix: 'RM',
  rowVersion: 'AQ==',
}

const bug = { id: 'lb1', name: 'Bug', color: '#B91C1C', emoji: null }

const lists = [
  ['l1', 'To do'],
  ['l2', 'Done'],
].map(([id, name], i) => ({
  id: id!,
  name: name!,
  emoji: null,
  position: (i + 1) * 1024,
  wipLimit: null,
  archivedAt: null,
}))

/** Noon on the 10th of the current month, so the card shows on this month's calendar. */
function tenth() {
  const now = new Date()
  return new Date(now.getFullYear(), now.getMonth(), 10, 12).toISOString()
}

type MockCard = Record<string, unknown> & {
  id: string
  listId: string
  title: string
  dueAt: string | null
  priority: string
  labels: (typeof bug)[]
}

async function mockApi(page: Page) {
  let version = 1
  const cards: MockCard[] = [
    ['c1', 'l1', 'Fix login', [bug], tenth()],
    ['c2', 'l1', 'Pick fonts', [], null],
    ['c3', 'l2', 'Crash on save', [bug], null],
  ].map(([id, listId, title, labels, dueAt], i) => ({
    id: id as string,
    listId: listId as string,
    number: i + 1,
    key: `RM-${i + 1}`,
    title: title as string,
    emoji: null,
    position: (i + 1) * 1024,
    dueAt: dueAt as string | null,
    startAt: null,
    priority: 'None',
    coverColor: null,
    completedAt: null,
    archivedAt: null,
    labels: labels as (typeof bug)[],
    assignees: [],
    checklistDone: 0,
    checklistTotal: 0,
    commentCount: 0,
    attachmentCount: 0,
    rowVersion: `v${version++}`,
  }))
  const queries: URLSearchParams[] = []

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      const { pathname } = url
      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards') return route.fulfill({ json: [board] })
      if (pathname === '/api/boards/b1') return route.fulfill({ json: board })
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [bug] })
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
          ],
        })
      }
      if (pathname === '/api/boards/b1/lists') {
        return route.fulfill({
          json: lists.map((l) => ({ ...l, cards: cards.filter((c) => c.listId === l.id) })),
        })
      }
      if (pathname === '/api/boards/b1/cards' && request.method() === 'GET') {
        const q = url.searchParams
        queries.push(q)
        const labels = q.get('labels')?.split(',') ?? []
        const from = q.get('dueFrom')
        const to = q.get('dueTo')
        const items = cards.filter(
          (c) =>
            (!labels.length || c.labels.some((l) => labels.includes(l.id))) &&
            (!from || (c.dueAt && c.dueAt >= from && c.dueAt < to!)),
        )
        return route.fulfill({ json: { items, truncated: false } })
      }
      const one = pathname.match(/^\/api\/boards\/b1\/cards\/(\w+)$/)
      if (one && request.method() === 'GET') {
        const card = cards.find((c) => c.id === one[1])!
        return route.fulfill({
          json: {
            card,
            description: card.id === 'c1' ? 'Users get **logged out** after a refresh.' : null,
            createdById: me.id,
            createdByName: me.displayName,
            createdAt: me.createdAt,
            checklist: [],
            attachments: [],
          },
        })
      }
      const complete = pathname.match(/^\/api\/boards\/b1\/cards\/(\w+)\/complete$/)
      if (complete) {
        const card = cards.find((c) => c.id === complete[1])!
        Object.assign(card, { completedAt: new Date().toISOString(), rowVersion: `v${version++}` })
        return route.fulfill({ json: card })
      }
      if (one && request.method() === 'PATCH') {
        const card = cards.find((c) => c.id === one[1])!
        Object.assign(card, request.postDataJSON(), { rowVersion: `v${version++}` })
        return route.fulfill({ json: card })
      }
      return route.fulfill({ status: 404 })
    },
  )
  return { cards, queries }
}

test('filters carry across Board, List and Calendar and survive reload', async ({ page }) => {
  const api = await mockApi(page)
  await page.goto('/boards/b1')

  await expect(page.getByText('Pick fonts')).toBeVisible()
  await page.getByRole('button', { name: 'Filter' }).click()
  await page
    .getByRole('dialog', { name: 'Filter tasks' })
    .getByRole('button', { name: 'Bug' })
    .click()
  await page.keyboard.press('Escape')

  await expect(page).toHaveURL(/labels=lb1/)
  await expect(page.getByText('Pick fonts')).toHaveCount(0)
  await expect(page.getByText('Fix login')).toBeVisible()
  await expect(page.getByRole('status').filter({ hasText: 'dragging is off' })).toBeVisible()

  await page.getByRole('tab', { name: 'List' }).click()
  await expect(page).toHaveURL(/view=list/)
  const table = page.getByRole('table')
  await expect(table.getByRole('button', { name: 'Fix login' })).toBeVisible()
  await expect(table.getByRole('button', { name: 'Crash on save' })).toBeVisible()
  await expect(table.getByText('Pick fonts')).toHaveCount(0)
  expect(api.queries.at(-1)?.get('labels')).toBe('lb1')

  await page.reload()
  await expect(page.getByRole('tab', { name: 'List' })).toHaveAttribute('aria-selected', 'true')
  await expect(page.getByRole('button', { name: 'Filter, 1 active' })).toBeVisible()
  await expect(page.getByRole('table').getByText('Pick fonts')).toHaveCount(0)

  await page.getByRole('combobox', { name: 'Priority of Fix login' }).selectOption('Urgent')
  await expect.poll(() => api.cards[0]!.priority).toBe('Urgent')
})

test('calendar moves a task to another day', async ({ page }) => {
  const api = await mockApi(page)
  await page.goto('/boards/b1?view=calendar')

  const chip = page.locator('[data-card-chip=c1]')
  await expect(chip).toBeVisible()
  await expect(page.getByText(/2 tasks have no due date/)).toBeVisible()
  await chip.focus()
  await page.keyboard.press('Alt+ArrowRight')

  await expect.poll(() => new Date(api.cards[0]!.dueAt!).getDate()).toBe(11)
  await expect(page.locator(`[data-day$="-11"] [data-card-chip=c1]`)).toBeVisible()
})

test('the last view is remembered per board', async ({ page }) => {
  await mockApi(page)
  await page.goto('/boards/b1')
  await page.getByRole('tab', { name: 'Calendar' }).click()
  await expect(page.locator('[data-card-chip=c1]')).toBeVisible()

  await page.goto('/boards/b1')

  await expect(page.getByRole('tab', { name: 'Calendar' })).toHaveAttribute('aria-selected', 'true')
})

test('calendar task preview shows details, completes and opens the task', async ({ page }) => {
  const api = await mockApi(page)
  await page.goto('/boards/b1?view=calendar')

  await page.locator('[data-card-chip=c1]').click()
  const preview = page.getByRole('dialog', { name: 'Preview of Fix login' })
  await expect(preview).toBeVisible()
  await expect(preview.getByText('Users get logged out after a refresh.')).toBeVisible()
  await expect(preview.getByText('Bug')).toBeVisible()

  await preview.getByRole('button', { name: 'Mark as done' }).click()
  await expect.poll(() => api.cards[0]!.completedAt).not.toBeNull()
  await expect(preview.getByRole('button', { name: 'Mark as not done' })).toBeVisible()

  await page.keyboard.press('Escape')
  await expect(preview).toHaveCount(0)
  await expect(page.locator('[data-card-chip=c1]')).toBeFocused()

  await page.locator('[data-card-chip=c1]').click()
  await page.getByRole('button', { name: 'Open task' }).click()
  await expect(page).toHaveURL(/\/boards\/b1\/cards\/c1\?view=calendar/)
})
