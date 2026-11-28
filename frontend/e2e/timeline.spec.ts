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

const lists = [
  ['l1', 'To do'],
  ['l2', 'Doing'],
].map(([id, name], i) => ({
  id: id!,
  name: name!,
  emoji: null,
  position: (i + 1) * 1024,
  wipLimit: null,
  archivedAt: null,
}))

const now = new Date()
const year = now.getFullYear()
const month = now.getMonth()
const daysInMonth = new Date(year, month + 1, 0).getDate()
const monthStart = `${year}-${String(month + 1).padStart(2, '0')}-01`
/** A local day of the current month at a given hour, as ISO. */
const day = (d: number, h = 12) => new Date(year, month, d, h).toISOString()

type MockCard = Record<string, unknown> & {
  id: string
  listId: string
  title: string
  startAt: string | null
  dueAt: string | null
}

async function mockApi(page: Page, role: 'Editor' | 'Viewer' = 'Editor') {
  const board = {
    id: 'b1',
    name: 'Roadmap',
    emoji: '🚀',
    description: null,
    color: '#0F766E',
    role,
    isStarred: false,
    memberCount: 1,
    archivedAt: null,
    keyPrefix: 'RM',
    rowVersion: 'AQ==',
  }
  let version = 1
  const cards: MockCard[] = (
    [
      ['c1', 'l1', 'Design login', day(3, 9), day(6, 17)],
      ['c2', 'l2', 'Ship login', null, day(12)],
      ['c3', 'l1', 'Someday idea', null, null],
    ] as const
  ).map(([id, listId, title, startAt, dueAt], i) => ({
    id,
    listId,
    number: i + 1,
    key: `RM-${i + 1}`,
    title,
    emoji: null,
    position: (i + 1) * 1024,
    dueAt,
    startAt,
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
    blocked: id === 'c2',
    blockerCount: id === 'c2' ? 1 : 0,
    blockingCount: id === 'c1' ? 1 : 0,
    rowVersion: `v${version++}`,
  }))
  const patches: Record<string, unknown>[] = []

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      const { pathname } = url
      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards') return route.fulfill({ json: [board] })
      if (pathname === '/api/boards/b1') return route.fulfill({ json: board })
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/members') {
        return route.fulfill({
          json: [
            {
              userId: me.id,
              displayName: me.displayName,
              email: me.email,
              avatarColor: me.avatarColor,
              role,
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
        const from = url.searchParams.get('from')!
        const to = url.searchParams.get('to')!
        const items = cards.filter((c) => {
          const start = c.startAt ?? c.dueAt
          const end = c.dueAt ?? c.startAt
          return start && end && start < to && end >= from
        })
        return route.fulfill({
          json: {
            items,
            truncated: false,
            dependencies: [{ cardId: 'c2', blockedById: 'c1' }],
          },
        })
      }
      const one = pathname.match(/^\/api\/boards\/b1\/cards\/(\w+)$/)
      if (one && request.method() === 'PATCH') {
        const card = cards.find((c) => c.id === one[1])!
        const body = request.postDataJSON() as Record<string, unknown>
        patches.push(body)
        Object.assign(card, body, { rowVersion: `v${version++}` })
        return route.fulfill({ json: card })
      }
      return route.fulfill({ status: 404 })
    },
  )
  return { cards, patches }
}

const localDay = (iso: string | null) => (iso ? new Date(iso).getDate() : null)

test('dragging a bar moves its dates and they stay after reload', async ({ page }) => {
  const api = await mockApi(page)
  await page.goto(`/boards/b1?view=timeline&from=${monthStart}`)

  const bar = page.locator('[data-bar=c1]')
  await expect(bar).toBeVisible()
  await expect(page.locator('[data-bar=c2]')).toBeVisible()
  await expect(page.getByTestId('timeline-arrows').locator('path.arrow')).toHaveCount(1)
  await page.getByText(/No dates · 1 task/).click()
  await expect(page.getByTestId('no-dates').getByText('Someday idea')).toBeVisible()

  const trackWidth = await bar.evaluate((el) => el.parentElement!.parentElement!.clientWidth)
  const box = (await bar.boundingBox())!
  const dayWidth = trackWidth / daysInMonth
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  await page.mouse.down()
  await page.mouse.move(box.x + box.width / 2 + dayWidth, box.y + box.height / 2, { steps: 4 })
  await page.mouse.move(box.x + box.width / 2 + dayWidth * 2, box.y + box.height / 2, { steps: 4 })
  await page.mouse.up()

  await expect.poll(() => localDay(api.cards[0]!.startAt)).toBe(5)
  expect(localDay(api.cards[0]!.dueAt)).toBe(8)
  expect(new Date(api.cards[0]!.dueAt!).getHours()).toBe(17)
  // A drag is not a click: the card sheet stays closed.
  await expect(page).not.toHaveURL(/\/cards\//)

  await page.reload()
  await expect(page.getByRole('tab', { name: 'Timeline' })).toHaveAttribute('aria-selected', 'true')
  await expect(page.locator('[data-bar=c1]')).toHaveAttribute('title', /–/)
  const moved = (await page.locator('[data-bar=c1]').boundingBox())!
  expect(moved.x - box.x).toBeGreaterThan(dayWidth * 1.5)
})

test('keyboard resizes the due date; zoom and window live in the URL', async ({ page }) => {
  const api = await mockApi(page)
  await page.goto(`/boards/b1?view=timeline&from=${monthStart}`)

  await page.locator('[data-bar=c1]').focus()
  await page.keyboard.press('Alt+Shift+ArrowRight')
  await expect.poll(() => localDay(api.cards[0]!.dueAt)).toBe(7)
  expect(api.patches.at(-1)).not.toHaveProperty('startAt')

  await page.getByRole('radio', { name: 'Week' }).click()
  await expect(page).toHaveURL(/zoom=week/)
  await page.getByRole('button', { name: 'Later' }).click()
  await expect(page).toHaveURL(/from=\d{4}-\d{2}-\d{2}/)
  await page.getByRole('button', { name: 'Today' }).click()
  await expect(page).not.toHaveURL(/from=/)
})

test('viewers can read the timeline but not drag it', async ({ page }) => {
  const api = await mockApi(page, 'Viewer')
  await page.goto(`/boards/b1?view=timeline&from=${monthStart}`)

  const bar = page.locator('[data-bar=c1]')
  await expect(bar).toBeVisible()
  await expect(page.locator('[data-handle]')).toHaveCount(0)
  const dates = await bar.getAttribute('title')
  const box = (await bar.boundingBox())!
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  await page.mouse.down()
  await page.mouse.move(box.x + box.width / 2 + 120, box.y + box.height / 2, { steps: 4 })
  await page.mouse.up()
  await bar.focus()
  await page.keyboard.press('Alt+ArrowRight')

  // Edits update the bar at once, so unchanged dates mean nothing was sent.
  await expect(bar).toHaveAttribute('title', dates!)
  expect(api.patches).toEqual([])
})

test('phones get an agenda that opens the card', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await mockApi(page)
  await page.goto(`/boards/b1?view=timeline&from=${monthStart}`)

  const agenda = page.getByTestId('timeline-agenda')
  await expect(agenda.getByRole('button', { name: /Design login/ })).toBeVisible()
  await expect(page.locator('[data-handle]')).toHaveCount(0)
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)

  await agenda.getByRole('button', { name: /Ship login/ }).click()
  await expect(page).toHaveURL(/\/boards\/b1\/cards\/c2/)
})
