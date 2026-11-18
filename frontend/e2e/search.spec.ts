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

const card = {
  id: 'c1',
  boardId: 'b1',
  key: 'RM-1',
  title: 'Write the spec',
  boardName: 'Roadmap',
  boardEmoji: '🚀',
  listName: 'To do',
  completedAt: null,
  keyMatch: false,
}

async function mockApi(page: Page) {
  const queries: string[] = []
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const { pathname, searchParams } = new URL(route.request().url())
      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/me/activity')
        return route.fulfill({ json: { items: [], nextCursor: null } })
      if (pathname === '/api/search') {
        const q = searchParams.get('q') ?? ''
        queries.push(q)
        const byKey = q.toUpperCase() === 'RM-1'
        const byTitle = 'write the spec'.includes(q.toLowerCase())
        return route.fulfill({
          json: {
            boards: q.toLowerCase().startsWith('road')
              ? [{ id: 'b1', name: 'Roadmap', emoji: '🚀', color: '#1D5BF5' }]
              : [],
            cards: byKey || byTitle ? [{ ...card, keyMatch: byKey }] : [],
            comments: [],
          },
        })
      }
      return route.fulfill({ status: 404, json: { title: `No mock for ${pathname}` } })
    },
  )
  return queries
}

test('Ctrl+K finds a card by name and Enter opens it', async ({ page }) => {
  const queries = await mockApi(page)
  await page.goto('/activity')
  await expect(page.getByRole('button', { name: 'Search' })).toBeVisible()

  await page.keyboard.press('ControlOrMeta+k')
  const palette = page.getByRole('dialog', { name: 'Search boards, cards and comments' })
  await expect(palette).toBeVisible()
  await page.keyboard.type('write')

  await expect(palette.getByRole('option', { name: /Write the spec/ })).toHaveAttribute(
    'aria-selected',
    'true',
  )
  expect(queries).toEqual(['write'])
  await page.keyboard.press('Enter')

  await expect(page).toHaveURL(/\/boards\/b1\/cards\/c1$/)
  await expect(palette).toHaveCount(0)
})

test('a card key comes first and picks show up as recent', async ({ page }) => {
  await mockApi(page)
  await page.goto('/activity')

  await page.getByRole('button', { name: 'Search' }).click()
  const palette = page.getByRole('dialog', { name: 'Search boards, cards and comments' })
  await expect(palette.getByText('Type at least 2 letters')).toBeVisible()
  await page.keyboard.type('rm-1')
  await expect(palette.getByRole('group', { name: 'Key match' })).toBeVisible()
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/boards\/b1\/cards\/c1$/)

  await page.goto('/activity')
  await expect(page.getByRole('button', { name: 'Search' })).toBeVisible()
  await page.keyboard.press('ControlOrMeta+k')
  const recent = palette.getByRole('group', { name: 'Recent' })
  await expect(recent.getByRole('option', { name: /Write the spec/ })).toBeVisible()

  await page.keyboard.press('Escape')
  await expect(palette).toHaveCount(0)
})
