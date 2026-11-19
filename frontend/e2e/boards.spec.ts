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

const roadmap = {
  id: 'b1',
  name: 'Roadmap',
  emoji: '🚀',
  description: 'Q1 plans',
  color: '#0F766E',
  role: 'Owner',
  isStarred: false,
  memberCount: 1,
  archivedAt: null,
}

/** Mocks the API with an in-memory list of boards. */
async function mockApi(page: Page, boards: (typeof roadmap)[]) {
  // A URL predicate, not a glob: '**/api/**' would also catch Vite's /src/shared/api/ modules.
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const { pathname } = new URL(request.url())
      const method = request.method()

      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards' && method === 'GET') return route.fulfill({ json: boards })
      if (pathname === '/api/boards' && method === 'POST') {
        const body = request.postDataJSON() as { name: string; emoji: string; color: string }
        const created = { ...roadmap, id: 'b2', ...body, description: null }
        boards.push({ ...created, isStarred: false, memberCount: 1, archivedAt: null })
        return route.fulfill({ status: 201, json: created })
      }
      const star = pathname.match(/^\/api\/boards\/(\w+)\/star$/)
      if (star) {
        const board = boards.find((b) => b.id === star[1])!
        board.isStarred = method === 'PUT'
        return route.fulfill({ status: 204 })
      }
      if (pathname.endsWith('/lists') || pathname.endsWith('/labels'))
        return route.fulfill({ json: [] })
      const detail = pathname.match(/^\/api\/boards\/(\w+)$/)
      if (detail) {
        const board = boards.find((b) => b.id === detail[1])!
        return route.fulfill({ json: { ...board, keyPrefix: 'RM', rowVersion: 'AQ==' } })
      }
      if (pathname.endsWith('/members')) {
        return route.fulfill({
          json: [
            {
              userId: me.id,
              displayName: me.displayName,
              email: me.email,
              avatarColor: me.avatarColor,
              role: 'Owner',
              joinedAt: me.createdAt,
            },
          ],
        })
      }
      if (pathname.endsWith('/invites') && method === 'GET') return route.fulfill({ json: [] })
      if (pathname.endsWith('/invites') && method === 'POST') {
        return route.fulfill({
          status: 201,
          json: {
            id: 'i1',
            token: 'tok123',
            email: null,
            role: 'Editor',
            expiresAt: '2026-11-25T00:00:00Z',
          },
        })
      }
      return route.fulfill({ status: 404 })
    },
  )
}

test('owner creates a board and makes an invite link', async ({ page }) => {
  await mockApi(page, [{ ...roadmap }])

  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Your boards' })).toBeVisible()
  await page.getByRole('button', { name: '+ New board' }).first().click()

  const sheet = page.getByRole('dialog', { name: 'New board' })
  await sheet.getByLabel('Board name').fill('Launch')
  await sheet.getByText('🎯').click()
  await expect(sheet.getByLabel('Emoji 🎯')).toBeChecked()
  await sheet.getByRole('button', { name: 'Create board' }).click()

  await expect(page).toHaveURL(/\/boards\/b2$/)
  await expect(page.getByRole('heading', { name: 'Launch' })).toBeVisible()

  await page.getByRole('button', { name: 'Board settings' }).click()
  const settings = page.getByRole('dialog', { name: 'Board settings' })
  await settings.getByRole('button', { name: 'Create invite link' }).click()
  await expect(settings.getByLabel('Invite link')).toHaveValue(/\/invite\/tok123$/)
  await expect(settings.getByRole('list', { name: 'Pending invites' })).toContainText('Link invite')
})

test('starring a board moves it first and marks it in the sidebar', async ({ page }) => {
  await mockApi(page, [{ ...roadmap, id: 'b0', name: 'Archive' }, { ...roadmap }])

  await page.goto('/')
  const sidebar = page.getByRole('navigation', { name: 'Main' })
  await expect(sidebar.getByRole('link', { name: /Roadmap/ })).not.toContainText('Starred')
  await page.getByRole('button', { name: 'Star Roadmap' }).click()

  await expect(page.getByRole('button', { name: 'Unstar Roadmap' })).toHaveCount(1)
  await expect(sidebar.getByRole('link', { name: /Roadmap/ })).toContainText('Starred')
  await expect(sidebar.getByRole('listitem').first()).toContainText('Roadmap')
})
