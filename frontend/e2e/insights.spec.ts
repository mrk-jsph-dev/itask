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
  role: 'Viewer',
  isStarred: false,
  memberCount: 2,
  archivedAt: null,
  keyPrefix: 'RM',
  rowVersion: 'AQ==',
}

function insights(days: number) {
  const dates = Array.from({ length: days }, (_, i) => {
    const d = new Date(Date.UTC(2026, 10, 26) - (days - 1 - i) * 86_400_000)
    return d.toISOString().slice(0, 10)
  })
  return {
    days,
    from: dates[0],
    to: dates[days - 1],
    throughput: dates.map((date, i) => ({ date, count: i % 3 === 0 ? 2 : 0 })),
    created: dates.map((date, i) => ({ date, count: i % 4 === 0 ? 1 : 0 })),
    cycleTime: { count: 20, medianDays: 4.5, p85Days: 9.2 },
    timeInList: [
      { listId: 'l1', name: 'To do', emoji: '📝', medianDays: 2, samples: 12 },
      { listId: 'l2', name: 'In progress', emoji: '🚧', medianDays: 3.5, samples: 9 },
      { listId: 'l3', name: 'Done', emoji: '✅', medianDays: null, samples: 0 },
    ],
    aging: [
      {
        id: 'c7',
        key: 'RM-7',
        title: 'Migrate billing',
        listId: 'l2',
        listName: 'In progress',
        days: 12.3,
      },
    ],
    wip: [
      { listId: 'l1', name: 'To do', emoji: '📝', open: 4, limit: null },
      { listId: 'l2', name: 'In progress', emoji: '🚧', open: 5, limit: 3 },
      { listId: 'l3', name: 'Done', emoji: '✅', open: 0, limit: null },
    ],
  }
}

async function mockApi(page: Page) {
  const requests: string[] = []
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const url = new URL(route.request().url())
      const { pathname, searchParams } = url
      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards/b1/insights') {
        requests.push(url.search)
        return route.fulfill({ json: insights(Number(searchParams.get('days'))) })
      }
      if (pathname === '/api/boards/b1') return route.fulfill({ json: board })
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/lists') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/members') return route.fulfill({ json: [] })
      return route.fulfill({ status: 404, json: { title: `No mock for ${pathname}` } })
    },
  )
  return requests
}

test('insights open from the board and switch range', async ({ page }) => {
  const requests = await mockApi(page)
  await page.goto('/boards/b1')

  await page.getByRole('link', { name: 'Board insights' }).click()
  await expect(page).toHaveURL(/\/boards\/b1\/insights$/)
  await expect(page.getByRole('heading', { name: '🚀 Roadmap insights' })).toBeVisible()

  const tiles = page.getByTestId('insight-tiles')
  await expect(tiles.getByText('Median cycle time')).toBeVisible()
  await expect(tiles.getByText('4.5 d')).toBeVisible()
  await expect(tiles.getByText('1 list over the limit')).toBeVisible()
  await expect(page.getByTestId('throughput-chart')).toBeVisible()
  await expect(page.getByText('5 open of 3 WIP · over the limit')).toBeVisible()
  await expect(page.getByRole('link', { name: /RM-7 Migrate billing/ })).toHaveAttribute(
    'href',
    '/boards/b1/cards/c7',
  )
  expect(requests[0]).toContain('days=30')

  await page.getByRole('button', { name: '60 days' }).click()
  await expect(page).toHaveURL(/days=60/)
  await expect(page.getByRole('button', { name: '60 days' })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(page.getByText('per week', { exact: true })).toBeVisible()
  expect(requests.at(-1)).toContain('days=60')
})

test('insights fit a phone without sideways scroll', async ({ page }) => {
  await mockApi(page)
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/boards/b1/insights?days=90')

  await expect(page.getByTestId('throughput-chart')).toBeVisible()
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  )
  expect(overflow).toBeLessThanOrEqual(0)
})
