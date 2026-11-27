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
  isStarred: false,
  memberCount: 1,
  archivedAt: null,
  keyPrefix: 'RM',
  rowVersion: 'AQ==',
}

interface MockCard {
  id: string
  number: number
  title: string
  completedAt: string | null
}

/** One board with two cards; RM-1 can wait on RM-2. Dependencies are kept server-side. */
async function mockApi(page: Page) {
  const cards: MockCard[] = [
    { id: 'c1', number: 1, title: 'Ship release', completedAt: null },
    { id: 'c2', number: 2, title: 'Write tests', completedAt: null },
  ]
  const links: { cardId: string; blockerId: string }[] = []
  const byId = (id: string) => cards.find((c) => c.id === id)!
  const open = (c: MockCard) => !c.completedAt
  const summary = (c: MockCard) => {
    const blockers = links.filter((l) => l.cardId === c.id).map((l) => byId(l.blockerId))
    const waiting = links.filter((l) => l.blockerId === c.id).map((l) => byId(l.cardId))
    const blockerCount = blockers.filter(open).length
    return {
      id: c.id,
      listId: 'l1',
      number: c.number,
      key: `RM-${c.number}`,
      title: c.title,
      emoji: null,
      position: c.number * 1024,
      dueAt: null,
      startAt: null,
      priority: 'None',
      coverColor: null,
      completedAt: c.completedAt,
      archivedAt: null,
      labels: [],
      assignees: [],
      checklistDone: 0,
      checklistTotal: 0,
      commentCount: 0,
      attachmentCount: 0,
      recurring: false,
      blocked: blockerCount > 0,
      blockerCount,
      blockingCount: waiting.filter(open).length,
      rowVersion: 'v1',
    }
  }

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const { pathname } = new URL(request.url())
      const method = request.method()

      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards/b1') return route.fulfill({ json: { ...board, role: 'Editor' } })
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/members') {
        return route.fulfill({
          json: [{ ...me, userId: me.id, role: 'Editor', joinedAt: me.createdAt }],
        })
      }
      if (pathname === '/api/boards/b1/lists') {
        return route.fulfill({
          json: [
            {
              id: 'l1',
              name: 'To do',
              emoji: '📝',
              position: 1024,
              wipLimit: null,
              archivedAt: null,
              cards: cards.map(summary),
            },
          ],
        })
      }

      const blockedBy = pathname.match(/^\/api\/boards\/b1\/cards\/(c\d)\/blocked-by\/(c\d)$/)
      if (blockedBy) {
        const [, cardId, blockerId] = blockedBy
        if (method === 'PUT') links.push({ cardId: cardId!, blockerId: blockerId! })
        else links.splice(links.findIndex((l) => l.cardId === cardId && l.blockerId === blockerId), 1)
        return route.fulfill({ json: summary(byId(cardId!)) })
      }

      const action = pathname.match(/^\/api\/boards\/b1\/cards\/(c\d)(?:\/(complete|reopen))?$/)
      if (action) {
        const card = byId(action[1]!)
        if (action[2] === 'complete') card.completedAt = '2026-11-27T12:00:00Z'
        if (action[2] === 'reopen') card.completedAt = null
        if (action[2]) return route.fulfill({ json: summary(card) })
        return route.fulfill({
          json: {
            card: summary(card),
            description: null,
            createdById: me.id,
            createdByName: me.displayName,
            createdAt: me.createdAt,
            checklist: [],
            attachments: [],
            recurrence: null,
            blockedBy: links
              .filter((l) => l.cardId === card.id)
              .map((l) => byId(l.blockerId))
              .map((b) => ({
                id: b.id,
                key: `RM-${b.number}`,
                title: b.title,
                completed: !!b.completedAt,
                archived: false,
              })),
          },
        })
      }
      if (pathname.endsWith('/comments')) return route.fulfill({ json: { items: [], nextCursor: null } })
      if (pathname === '/api/notifications/unread-count') return route.fulfill({ json: { count: 0 } })
      if (pathname === '/api/ai/status') return route.fulfill({ json: { enabled: false } })
      if (pathname === '/api/boards/b1/automations') return route.fulfill({ json: [] })
      if (pathname === '/api/boards') return route.fulfill({ json: [{ ...board, role: 'Editor' }] })

      return route.fulfill({ status: 404, json: { title: `No mock for ${method} ${pathname}` } })
    },
  )
}

test('a blocked card shows 🔒 until its blocker is completed', async ({ page }) => {
  await mockApi(page)
  await page.goto('/boards/b1/cards/c1')

  const sheet = page.getByRole('dialog', { name: 'RM-1' })
  await sheet.getByRole('button', { name: '+ Add blocker' }).click()
  await sheet.getByRole('combobox', { name: 'Find a card this one waits on' }).fill('rm-2')
  await sheet.getByRole('option', { name: /RM-2\s*Write tests/ }).click()
  await expect(sheet.getByRole('list', { name: 'Blockers' })).toContainText('Write tests')
  await sheet.getByRole('button', { name: 'Close' }).click()

  const blocked = page.getByRole('article', { name: 'RM-1 Ship release' })
  await expect(blocked.getByRole('img', { name: 'Blocked by 1 card' })).toBeVisible()
  await expect(page.getByRole('article', { name: 'RM-2 Write tests' })).toContainText('blocks 1')

  await page.getByRole('button', { name: 'Write tests card menu' }).click()
  await page.getByRole('menuitem', { name: '✓ Mark complete' }).click()

  await expect(blocked.getByRole('img', { name: /Blocked by/ })).toHaveCount(0)
})
