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
  isStarred: false,
  memberCount: 1,
  archivedAt: null,
  keyPrefix: 'RM',
  rowVersion: 'AQ==',
}

interface MockCard {
  id: string
  listId: string
  number: number
  key: string
  title: string
  position: number
  dueAt: string | null
  startAt: string | null
  priority: string
  coverColor: string | null
  completedAt: string | null
  archivedAt: string | null
  labels: { id: string; name: string; color: string; emoji: string | null }[]
  assignees: { userId: string; displayName: string; avatarColor: string }[]
  checklistDone: number
  checklistTotal: number
  commentCount: number
  rowVersion: string
}

const lists = [
  ['l1', '📝', 'To do'],
  ['l2', '🚧', 'In progress'],
  ['l3', '✅', 'Done'],
].map(([id, emoji, name], i) => ({
  id: id!,
  name: name!,
  emoji: emoji!,
  position: (i + 1) * 1024,
  wipLimit: null,
  archivedAt: null,
}))

/** Mocks the API with server-side cards, so reloads see saved moves. */
async function mockApi(page: Page, role = 'Editor') {
  let next = 1
  let version = 1
  const cards: MockCard[] = []
  const newCard = (listId: string, title: string, extra: Partial<MockCard> = {}): MockCard => {
    const inList = cards.filter((c) => c.listId === listId)
    const card: MockCard = {
      id: `c${next}`,
      listId,
      number: next,
      key: `RM-${next}`,
      title,
      position: (inList.at(-1)?.position ?? 0) + 1024,
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
      rowVersion: `v${version++}`,
      ...extra,
    }
    next++
    cards.push(card)
    return card
  }
  newCard('l1', 'Write the spec', {
    priority: 'High',
    dueAt: '2020-01-01T23:59:59Z',
    labels: [{ id: 'lb1', name: 'Backend', color: '#7C3AED', emoji: '⚙️' }],
    assignees: [{ userId: me.id, displayName: me.displayName, avatarColor: me.avatarColor }],
  })
  newCard('l1', 'Pick fonts')

  const inList = (listId: string) =>
    cards
      .filter((c) => c.listId === listId && !c.archivedAt)
      .sort((a, b) => a.position - b.position)

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const { pathname } = new URL(request.url())
      const method = request.method()

      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards/b1') return route.fulfill({ json: { ...board, role } })
      if (pathname === '/api/boards/b1/labels') {
        return route.fulfill({
          json: [{ id: 'lb1', name: 'Backend', color: '#7C3AED', emoji: '⚙️' }],
        })
      }
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
        return route.fulfill({ json: lists.map((l) => ({ ...l, cards: inList(l.id) })) })
      }
      const create = pathname.match(/^\/api\/boards\/b1\/lists\/(\w+)\/cards$/)
      if (create && method === 'POST') {
        const { title } = request.postDataJSON() as { title: string }
        return route.fulfill({ status: 201, json: newCard(create[1]!, title) })
      }
      const one = pathname.match(/^\/api\/boards\/b1\/cards\/(\w+)(?:\/(\w+))?$/)
      if (one) {
        const card = cards.find((c) => c.id === one[1])!
        if (one[2] === 'position') {
          const body = request.postDataJSON() as {
            listId: string
            beforeId: string | null
            afterId: string | null
            rowVersion: string
          }
          if (body.rowVersion !== card.rowVersion) {
            return route.fulfill({
              status: 409,
              json: { title: 'The card was changed by someone else.' },
            })
          }
          const before = cards.find((c) => c.id === body.beforeId)?.position ?? 0
          const after = cards.find((c) => c.id === body.afterId)?.position
          card.listId = body.listId
          card.position = after === undefined ? before + 1024 : (before + after) / 2
          card.rowVersion = `v${version++}`
          return route.fulfill({ json: { card, renumbered: false } })
        }
        if (method === 'GET')
          return route.fulfill({
            json: {
              card,
              description: null,
              createdById: me.id,
              createdByName: me.displayName,
              createdAt: me.createdAt,
            },
          })
        if (one[2] === 'archive') card.archivedAt = '2026-11-18T12:00:00Z'
        else if (one[2] === 'restore') card.archivedAt = null
        else if (one[2] === 'complete') card.completedAt = '2026-11-18T12:00:00Z'
        else if (one[2] === 'reopen') card.completedAt = null
        else if (method === 'PATCH') Object.assign(card, request.postDataJSON())
        card.rowVersion = `v${version++}`
        return route.fulfill({ json: card })
      }
      return route.fulfill({ status: 404 })
    },
  )
}

const cardTitles = (page: Page, list: string) =>
  page
    .getByRole('list', { name: `${list} cards` })
    .locator('[data-card-id] .card-open > span:last-child')
    .allTextContents()

test('editor creates a card and drags it to another list', async ({ page }) => {
  await mockApi(page)
  await page.goto('/boards/b1')

  await expect.poll(() => cardTitles(page, 'To do')).toEqual(['Write the spec', 'Pick fonts'])
  const tile = page.getByRole('article', { name: 'RM-1 Write the spec' })
  await expect(tile.getByText('Overdue,')).toBeAttached()
  await expect(tile.getByText('High')).toBeVisible()
  await expect(tile.getByRole('list', { name: 'Labels' })).toContainText('Backend')

  await page.getByRole('button', { name: 'Add card to In progress' }).click()
  await page.getByLabel('Card title').fill('Ship it')
  await page.keyboard.press('Enter')
  await expect.poll(() => cardTitles(page, 'In progress')).toEqual(['Ship it'])
  await page.keyboard.press('Escape')

  const from = await page.getByRole('article', { name: 'RM-3 Ship it' }).boundingBox()
  const to = await page.getByRole('list', { name: 'Done cards' }).boundingBox()
  await page.mouse.move(from!.x + 20, from!.y + 20)
  await page.mouse.down()
  for (let step = 1; step <= 12; step++) {
    await page.mouse.move(
      from!.x + 20 + ((to!.x + 40 - from!.x - 20) * step) / 12,
      from!.y + 20 + ((to!.y + 30 - from!.y - 20) * step) / 12,
    )
  }
  const saved = page.waitForResponse((r) => r.url().endsWith('/position') && r.ok())
  await page.mouse.up()
  await saved

  await expect.poll(() => cardTitles(page, 'Done')).toEqual(['Ship it'])
  await expect.poll(() => cardTitles(page, 'In progress')).toEqual([])
  await page.reload()
  await expect.poll(() => cardTitles(page, 'Done')).toEqual(['Ship it'])
})

test('keyboard moves a card to another list and up within it', async ({ page }) => {
  await mockApi(page)
  await page.goto('/boards/b1')
  await expect.poll(() => cardTitles(page, 'To do')).toHaveLength(2)

  await page.getByRole('button', { name: 'Pick fonts card menu' }).click()
  await page.getByRole('menuitem', { name: /Move to list/ }).click()
  await page.getByRole('menuitem', { name: /Done/ }).click()
  await expect.poll(() => cardTitles(page, 'Done')).toEqual(['Pick fonts'])

  await page.getByRole('button', { name: 'Write the spec card menu' }).click()
  await page.getByRole('menuitem', { name: /Move to list/ }).click()
  await page.getByRole('menuitem', { name: /Done/ }).click()
  await expect.poll(() => cardTitles(page, 'Done')).toEqual(['Pick fonts', 'Write the spec'])

  await page.getByRole('button', { name: 'Write the spec card menu' }).focus()
  await page.keyboard.press('Enter')
  await page.keyboard.press('ArrowDown')
  await expect(page.getByRole('menuitem', { name: /Move up/ })).toBeFocused()
  const saved = page.waitForResponse((r) => r.url().endsWith('/position') && r.ok())
  await page.keyboard.press('Enter')
  await saved
  await expect.poll(() => cardTitles(page, 'Done')).toEqual(['Write the spec', 'Pick fonts'])

  await page.reload()
  await expect.poll(() => cardTitles(page, 'Done')).toEqual(['Write the spec', 'Pick fonts'])
})

test('editor archives a card and undoes it', async ({ page }) => {
  await mockApi(page)
  await page.goto('/boards/b1')
  await expect.poll(() => cardTitles(page, 'To do')).toHaveLength(2)

  await page.getByRole('button', { name: 'Pick fonts card menu' }).click()
  await page.getByRole('menuitem', { name: /Archive/ }).click()
  await expect.poll(() => cardTitles(page, 'To do')).toEqual(['Write the spec'])
  await page.getByRole('button', { name: 'Undo' }).click()
  await expect.poll(() => cardTitles(page, 'To do')).toEqual(['Write the spec', 'Pick fonts'])
})

test('card sheet saves priority', async ({ page }) => {
  await mockApi(page)
  await page.goto('/boards/b1')

  await page
    .getByRole('button', { name: /Pick fonts/ })
    .first()
    .click()
  const sheet = page.getByRole('dialog', { name: 'RM-2' })
  await sheet.getByLabel('Priority').selectOption('Urgent')
  await sheet.getByRole('button', { name: 'Close' }).click()

  await expect(
    page.getByRole('article', { name: 'RM-2 Pick fonts' }).getByText('Urgent'),
  ).toBeVisible()
})

test('viewers see cards without editing controls', async ({ page }) => {
  await mockApi(page, 'Viewer')
  await page.goto('/boards/b1')

  await expect.poll(() => cardTitles(page, 'To do')).toHaveLength(2)
  await expect(page.getByRole('button', { name: /card menu/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /Add card/ })).toHaveCount(0)
})
