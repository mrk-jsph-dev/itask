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
  memberCount: 1,
  archivedAt: null,
  keyPrefix: 'RM',
  rowVersion: 'AQ==',
}

interface MockList {
  id: string
  name: string
  emoji: string | null
  position: number
  wipLimit: number | null
  archivedAt: string | null
  cards: unknown[]
}

/** Mocks the API with a server-side list store, so reloads see saved changes. */
async function mockApi(page: Page, role = 'Editor') {
  const lists: MockList[] = [
    ['l1', '📝', 'To do'],
    ['l2', '🚧', 'In progress'],
    ['l3', '👀', 'Review'],
    ['l4', '✅', 'Done'],
  ].map(([id, emoji, name], i) => ({
    id: id!,
    name: name!,
    emoji: emoji!,
    position: (i + 1) * 1024,
    wipLimit: null,
    archivedAt: null,
    cards: [],
  }))
  const active = () => lists.filter((l) => !l.archivedAt).sort((a, b) => a.position - b.position)

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const { pathname, searchParams } = new URL(request.url())
      const method = request.method()

      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards/b1') return route.fulfill({ json: { ...board, role } })
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
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/lists' && method === 'GET') {
        const archived = searchParams.get('archived') === 'true'
        return route.fulfill({ json: archived ? lists.filter((l) => l.archivedAt) : active() })
      }
      if (pathname === '/api/boards/b1/lists' && method === 'POST') {
        const body = request.postDataJSON() as Pick<MockList, 'name' | 'emoji' | 'wipLimit'>
        const created = {
          id: `l${lists.length + 1}`,
          ...body,
          position: (active().at(-1)?.position ?? 0) + 1024,
          archivedAt: null,
          cards: [],
        }
        lists.push(created)
        return route.fulfill({ status: 201, json: created })
      }
      const one = pathname.match(/^\/api\/boards\/b1\/lists\/(\w+)(?:\/(\w+))?$/)
      if (one) {
        const list = lists.find((l) => l.id === one[1])!
        if (one[2] === 'position') {
          const { beforeId, afterId } = request.postDataJSON() as {
            beforeId: string | null
            afterId: string | null
          }
          const before = lists.find((l) => l.id === beforeId)?.position ?? 0
          const after = lists.find((l) => l.id === afterId)?.position
          list.position = after === undefined ? before + 1024 : (before + after) / 2
          return route.fulfill({ json: { id: list.id, position: list.position } })
        }
        if (one[2] === 'archive') list.archivedAt = '2026-11-18T12:00:00Z'
        else if (one[2] === 'restore') {
          list.archivedAt = null
          list.position = (active().at(-1)?.position ?? 0) + 1024
        } else Object.assign(list, request.postDataJSON())
        return one[2] ? route.fulfill({ status: 204 }) : route.fulfill({ json: list })
      }
      return route.fulfill({ status: 404 })
    },
  )
}

const columnNames = (page: Page) => page.locator('[data-list] h2').allTextContents()

test('editor adds, renames, moves and archives lists', async ({ page }) => {
  await mockApi(page)
  await page.goto('/boards/b1')

  await expect.poll(() => columnNames(page)).toEqual(['To do', 'In progress', 'Review', 'Done'])

  await page.getByRole('button', { name: '+ Add list' }).click()
  await page.getByLabel('List name').fill('Blocked')
  await page.getByText('🐞', { exact: true }).click()
  await expect(page.getByLabel('Emoji 🐞')).toBeChecked()
  await page.getByRole('button', { name: 'Add list', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Blocked' })).toBeVisible()

  await page.getByRole('button', { name: 'To do list menu' }).click()
  await page.getByRole('menuitem', { name: /Rename/ }).click()
  await page.getByLabel('List name').fill('Backlog')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('heading', { name: 'Backlog' })).toBeVisible()

  // Keyboard path for reordering: open the menu, arrow to "Move right", press Enter.
  await page.getByRole('button', { name: 'Backlog list menu' }).focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('menuitem', { name: /Rename/ })).toBeFocused()
  // "Move left" is disabled on the first column, so it is skipped.
  await page.keyboard.press('ArrowDown')
  await expect(page.getByRole('menuitem', { name: /Move right/ })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect
    .poll(() => columnNames(page))
    .toEqual(['In progress', 'Backlog', 'Review', 'Done', 'Blocked'])

  await page.reload()
  await expect
    .poll(() => columnNames(page))
    .toEqual(['In progress', 'Backlog', 'Review', 'Done', 'Blocked'])

  await page.getByRole('button', { name: 'Review list menu' }).click()
  await page.getByRole('menuitem', { name: /Archive/ }).click()
  await expect(page.getByRole('heading', { name: 'Review' })).toBeHidden()
  await page.getByRole('button', { name: 'Undo' }).click()
  await expect
    .poll(() => columnNames(page))
    .toEqual(['In progress', 'Backlog', 'Done', 'Blocked', 'Review'])
})

test('viewers see lists without editing controls', async ({ page }) => {
  await mockApi(page, 'Viewer')
  await page.goto('/boards/b1')

  await expect.poll(() => columnNames(page)).toHaveLength(4)
  await expect(page.getByRole('button', { name: /list menu/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '+ Add list' })).toHaveCount(0)
})

for (const width of [1280, 390]) {
  test(`columns wrap without sideways scroll at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await mockApi(page)
    await page.goto('/boards/b1')
    await expect.poll(() => columnNames(page)).toHaveLength(4)

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    )
    expect(overflow).toBeLessThanOrEqual(0)
  })
}

test('dragging a column header reorders the lists', async ({ page }) => {
  await mockApi(page)
  await page.goto('/boards/b1')
  await expect.poll(() => columnNames(page)).toHaveLength(4)

  const from = await page.getByRole('heading', { name: 'Done' }).boundingBox()
  const to = await page.getByRole('heading', { name: 'To do' }).boundingBox()
  await page.mouse.move(from!.x + 5, from!.y + 5)
  await page.mouse.down()
  for (let step = 1; step <= 10; step++) {
    await page.mouse.move(
      from!.x + ((to!.x - from!.x) * step) / 10,
      from!.y + ((to!.y - from!.y) * step) / 10,
    )
  }
  await page.mouse.up()

  await expect.poll(() => columnNames(page)).toEqual(['Done', 'To do', 'In progress', 'Review'])
  await page.reload()
  await expect.poll(() => columnNames(page)).toEqual(['Done', 'To do', 'In progress', 'Review'])
})
