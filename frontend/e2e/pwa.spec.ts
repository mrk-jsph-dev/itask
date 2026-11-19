import { test, expect, type BrowserContext, type Page } from '@playwright/test'

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
  isStarred: false,
  memberCount: 1,
  archivedAt: null,
  keyPrefix: 'RM',
  rowVersion: 'AQ==',
  role: 'Editor',
}

const card = {
  id: 'c1',
  listId: 'l1',
  number: 1,
  key: 'RM-1',
  title: 'Write the spec',
  emoji: null,
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
  commentCount: 0,
  attachmentCount: 0,
  recurring: false,
  rowVersion: 'v1',
}

const lists = [
  {
    id: 'l1',
    name: 'To do',
    emoji: '📝',
    position: 1024,
    wipLimit: null,
    archivedAt: null,
    cards: [card],
  },
]

/** Mocks the API on the context, so requests the service worker makes are answered too. */
async function mockApi(target: Page | BrowserContext) {
  await target.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const { pathname } = new URL(route.request().url())
      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards') return route.fulfill({ json: [board] })
      if (pathname === '/api/boards/b1') return route.fulfill({ json: board })
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/members') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/lists') return route.fulfill({ json: lists })
      return route.fulfill({ status: 404 })
    },
  )
}

test('offline, the board is read-only with a banner and disabled actions', async ({
  page,
  context,
}) => {
  await mockApi(page)
  await page.goto('/boards/b1')
  await expect(page.getByText('Write the spec')).toBeVisible()
  const newTask = page.getByRole('button', { name: '+ New task' })
  await expect(newTask).toBeEnabled()

  await context.setOffline(true)

  await expect(page.getByTestId('offline-banner')).toContainText('read-only')
  await expect(newTask).toBeDisabled()
  await expect(newTask).toHaveAttribute('title', "You're offline. Changes are disabled.")
  await expect(page.getByRole('button', { name: '+ Add list' })).toHaveCount(0)

  await context.setOffline(false)
  await expect(page.getByTestId('offline-banner')).toHaveCount(0)
  await expect(newTask).toBeEnabled()
})

test.describe('installed app (production build)', () => {
  // Only the built app has a service worker; CI runs Playwright against `vite preview`.
  // eslint-disable-next-line playwright/no-skipped-test -- dev mode has no service worker
  test.skip(!process.env.CI, 'needs the production build (CI=1 after vite build)')
  test.use({ serviceWorkers: 'allow' })

  test('is installable and opens a viewed board offline', async ({ page, context }) => {
    await mockApi(context)
    await page.goto('/')
    await page.evaluate(async () => {
      await navigator.serviceWorker.ready
      if (!navigator.serviceWorker.controller) {
        await new Promise((resolve) =>
          navigator.serviceWorker.addEventListener('controllerchange', resolve, { once: true }),
        )
      }
    })

    const manifest = await (await page.request.get('/manifest.webmanifest')).json()
    expect(manifest).toMatchObject({ name: 'iTask', display: 'standalone', theme_color: '#1D5BF5' })
    const cdp = await context.newCDPSession(page)
    const { installabilityErrors } = await cdp.send('Page.getInstallabilityErrors')
    // Playwright contexts are incognito, which Chrome never installs from; anything else is ours.
    expect(installabilityErrors.filter((e) => e.errorId !== 'in-incognito')).toEqual([])

    // View the board online, so the service worker caches it.
    await page
      .getByRole('link', { name: /Roadmap/ })
      .first()
      .click()
    await expect(page.getByText('Write the spec')).toBeVisible()

    // What the app loaded before the worker took over is fetched again and cached.
    await expect
      .poll(() =>
        page.evaluate(async () =>
          (await (await caches.open('itask-api')).keys()).map((r) => new URL(r.url).pathname),
        ),
      )
      .toEqual(
        expect.arrayContaining([
          '/api/me',
          '/api/boards',
          '/api/boards/b1',
          '/api/boards/b1/lists',
        ]),
      )

    await context.unrouteAll()
    await context.route('**/api/**', (route) => route.abort('internetdisconnected'))
    await context.setOffline(true)
    await page.reload()

    await expect(page.getByText('Write the spec')).toBeVisible()
    await expect(page.getByTestId('offline-banner')).toBeVisible()
    await expect(page.getByRole('button', { name: '+ New task' })).toBeDisabled()
    await expect(page.getByRole('link', { name: /Roadmap/ }).first()).toBeVisible()
  })
})
