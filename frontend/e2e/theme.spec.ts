import AxeBuilder from '@axe-core/playwright'
import { test, expect, type Page } from '@playwright/test'

const me = {
  id: 'u1',
  email: 'ada@example.com',
  displayName: 'Ada Lovelace',
  avatarColor: '#1D5BF5',
  timeZone: 'UTC',
  themePreference: 'System',
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
  memberCount: 1,
  archivedAt: null,
  keyPrefix: 'RM',
  rowVersion: 'AQ==',
}

const labels = [
  { id: 'lb1', name: 'Bug', color: '#B91C1C', emoji: null },
  { id: 'lb2', name: 'Docs', color: '#374151', emoji: null },
  { id: 'lb3', name: 'Design', color: '#7C3AED', emoji: '🎨' },
]

const lists = ['To do', 'In progress', 'Done'].map((name, i) => ({
  id: `l${i + 1}`,
  name,
  emoji: ['📝', '🚧', '✅'][i],
  position: (i + 1) * 1024,
  wipLimit: null,
  archivedAt: null,
}))

function day(n: number) {
  const now = new Date()
  return new Date(now.getFullYear(), now.getMonth(), n, 12).toISOString()
}

const cards = [
  ['c1', 'l1', 'Fix login', [labels[0]], day(10), 'High', null],
  ['c2', 'l1', 'Write the guide', [labels[1]], day(12), 'Low', null],
  ['c3', 'l2', 'New icons', [labels[2]], day(14), 'Urgent', null],
  ['c4', 'l2', 'Pick fonts', [], day(3), 'Medium', null],
  ['c5', 'l3', 'Scaffold', [labels[1]], day(5), 'None', day(5)],
].map(([id, listId, title, cardLabels, dueAt, priority, completedAt], i) => ({
  id,
  listId,
  number: i + 1,
  key: `RM-${i + 1}`,
  title,
  emoji: null,
  position: (i + 1) * 1024,
  dueAt,
  startAt: null,
  priority,
  coverColor: null,
  completedAt,
  archivedAt: null,
  labels: cardLabels,
  assignees: [{ userId: me.id, displayName: me.displayName, avatarColor: me.avatarColor }],
  checklistDone: 1,
  checklistTotal: 3,
  commentCount: 0,
  attachmentCount: 0,
  recurring: false,
  blocked: i === 0,
  blockerCount: i === 0 ? 1 : 0,
  blockingCount: 0,
  rowVersion: `v${i}`,
}))

function insights() {
  const dates = Array.from({ length: 30 }, (_, i) =>
    new Date(Date.UTC(2026, 10, 26) - (29 - i) * 86_400_000).toISOString().slice(0, 10),
  )
  return {
    days: 30,
    from: dates[0],
    to: dates[29],
    throughput: dates.map((date, i) => ({ date, count: i % 3 === 0 ? 2 : 0 })),
    created: dates.map((date, i) => ({ date, count: i % 4 === 0 ? 1 : 0 })),
    cycleTime: { count: 20, medianDays: 4.5, p85Days: 9.2 },
    timeInList: lists.map((l, i) => ({
      listId: l.id,
      name: l.name,
      emoji: l.emoji,
      medianDays: i + 1,
      samples: 5,
    })),
    aging: [
      {
        id: 'c3',
        key: 'RM-3',
        title: 'New icons',
        listId: 'l2',
        listName: 'In progress',
        days: 12.3,
      },
    ],
    wip: lists.map((l, i) => ({
      listId: l.id,
      name: l.name,
      emoji: l.emoji,
      open: 4,
      limit: i === 1 ? 3 : null,
    })),
  }
}

async function mockApi(page: Page, user = me) {
  const saved: unknown[] = []
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const { pathname } = new URL(request.url())
      const method = request.method()
      if (pathname === '/api/me' && method === 'PATCH') {
        saved.push(request.postDataJSON())
        return route.fulfill({ json: request.postDataJSON() })
      }
      if (pathname === '/api/me') return route.fulfill({ json: user })
      if (pathname === '/api/me/stats')
        return route.fulfill({ json: { boards: 1, tasks: 5, done: 1 } })
      if (pathname === '/api/me/due-soon')
        return route.fulfill({ json: { overdue: 0, dueThisWeek: 0, items: [] } })
      if (pathname === '/api/me/logins')
        return route.fulfill({ json: { hasPassword: true, logins: [], providers: [] } })
      if (pathname === '/api/me/notification-preferences')
        return route.fulfill({
          json: {
            digest: 'Off',
            types: {
              mentioned: true,
              assigned: true,
              dueSoon: true,
              overdue: true,
              invited: true,
              unblocked: true,
            },
            emailConfirmed: true,
          },
        })
      if (pathname === '/api/notifications/unread-count')
        return route.fulfill({ json: { count: 2 } })
      if (pathname === '/api/ai/status') return route.fulfill({ json: { enabled: false } })
      if (pathname === '/api/boards') return route.fulfill({ json: [board] })
      if (pathname === '/api/boards/b1') return route.fulfill({ json: board })
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: labels })
      if (pathname === '/api/boards/b1/insights') return route.fulfill({ json: insights() })
      if (pathname === '/api/boards/b1/members')
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
      if (pathname === '/api/boards/b1/lists')
        return route.fulfill({
          json: lists.map((l) => ({ ...l, cards: cards.filter((c) => c.listId === l.id) })),
        })
      if (pathname === '/api/boards/b1/cards')
        return route.fulfill({ json: { items: cards, truncated: false } })
      const one = pathname.match(/^\/api\/boards\/b1\/cards\/(\w+)$/)
      if (one) {
        const card = cards.find((c) => c.id === one[1])!
        return route.fulfill({
          json: {
            card,
            description:
              'Users get **logged out** after a refresh. See `auth.ts`.\n\n> Seen on Safari.',
            createdById: me.id,
            createdByName: me.displayName,
            createdAt: me.createdAt,
            checklist: [
              { id: 'i1', text: 'Reproduce', isDone: true, position: 1024 },
              { id: 'i2', text: 'Fix the cookie', isDone: false, position: 2048 },
            ],
            attachments: [],
            recurrence: null,
            blockers: [],
            blocking: [],
          },
        })
      }
      if (/\/(comments|activity)$/.test(pathname))
        return route.fulfill({ json: { items: [], nextCursor: null } })
      if (method === 'GET') return route.fulfill({ json: [] })
      return route.fulfill({ status: 404 })
    },
  )
  return { saved }
}

const theme = (page: Page) => page.evaluate(() => document.documentElement.dataset.theme ?? null)

async function expectContrast(page: Page, label: string) {
  const results = await new AxeBuilder({ page }).withRules(['color-contrast']).analyze()
  const hits = results.violations.flatMap((v) =>
    v.nodes.map((n) => `${n.target.join(' ')}: ${n.failureSummary?.split('\n').at(-1)?.trim()}`),
  )
  expect(hits, `${label} contrast`).toEqual([])
}

test('Dark persists across reloads and is set before the app paints', async ({ page }) => {
  const api = await mockApi(page)
  await page.goto('/profile')

  await page.getByRole('group', { name: 'Theme' }).getByText('Dark', { exact: true }).click()
  await expect.poll(() => theme(page)).toBe('dark')
  await expect.poll(() => api.saved).toEqual([{ themePreference: 'Dark' }])

  // The server now returns Dark; record data-theme as soon as the document is parsed.
  await page.unroute((url) => url.pathname.startsWith('/api/'))
  await mockApi(page, { ...me, themePreference: 'Dark' })
  await page.addInitScript(() => {
    document.addEventListener('DOMContentLoaded', () => {
      ;(window as unknown as { firstTheme: string | undefined }).firstTheme =
        document.documentElement.dataset.theme
    })
  })
  await page.reload()
  expect(await page.evaluate(() => (window as unknown as { firstTheme?: string }).firstTheme)).toBe(
    'dark',
  )
  await expect(page.getByRole('radio', { name: 'Dark' })).toBeChecked()
  await expect(page.locator('meta[name="theme-color"]').first()).toHaveAttribute(
    'content',
    '#0E131B',
  )
})

test('the profile value wins over this device once signed in', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('itask.theme', 'Light'))
  await mockApi(page, { ...me, themePreference: 'Dark' })
  await page.goto('/')

  await expect.poll(() => theme(page)).toBe('dark')
})

test('sign-in follows the system theme when nothing is saved', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' })
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    (route) => route.fulfill({ status: 401 }),
  )
  await page.goto('/sign-in')

  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible()
  expect(await theme(page)).toBeNull()
  const shell = await page.evaluate(() => getComputedStyle(document.body).backgroundColor)
  expect(shell).toBe('rgb(14, 19, 27)')
})

for (const mode of ['Light', 'Dark'] as const) {
  test(`${mode}: board, card sheet, calendar and insights pass contrast`, async ({ page }) => {
    await page.addInitScript((m) => localStorage.setItem('itask.theme', m), mode)
    await mockApi(page, { ...me, themePreference: mode })
    // Animations would sample half-faded text.
    await page.emulateMedia({ reducedMotion: 'reduce' })

    await page.goto('/boards/b1')
    await expect(page.getByText('Write the guide')).toBeVisible()
    await expectContrast(page, `${mode} board`)

    await page.goto('/boards/b1/cards/c1')
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByText('Fix the cookie')).toBeVisible()
    await expectContrast(page, `${mode} card sheet`)

    await page.goto('/boards/b1?view=calendar')
    await expect(page.getByRole('grid')).toBeVisible()
    await expectContrast(page, `${mode} calendar`)

    await page.goto('/boards/b1/insights')
    await expect(page.getByText('New icons')).toBeVisible()
    await expectContrast(page, `${mode} insights`)
  })
}
