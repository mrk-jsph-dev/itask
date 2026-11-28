import { test, expect, type Page, type WebSocketRoute } from '@playwright/test'

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

const bo = { userId: 'u2', displayName: 'Bo Chen', avatarColor: '#0F766E' }

const board = {
  id: 'b1',
  name: 'Roadmap',
  emoji: '🚀',
  description: null,
  color: '#0F766E',
  isStarred: false,
  memberCount: 2,
  archivedAt: null,
  keyPrefix: 'RM',
  rowVersion: 'AQ==',
  role: 'Editor',
}

const lists = [
  ['l1', '📝', 'To do'],
  ['l2', '🚧', 'In progress'],
].map(([id, emoji, name], i) => ({
  id: id!,
  name: name!,
  emoji: emoji!,
  position: (i + 1) * 1024,
  wipLimit: null,
  archivedAt: null,
}))

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
  blocked: false,
  blockerCount: 0,
  blockingCount: 0,
  rowVersion: 'v1',
}

/** SignalR's JSON protocol: records end with the 0x1E separator. */
const record = (message: unknown) => `${JSON.stringify(message)}\u001e`

/** Mocks the API and the board hub; returns a way to push hub messages. */
async function mockApi(page: Page) {
  let socket: WebSocketRoute | null = null
  let joined = false

  await page.routeWebSocket(/\/api\/hubs\/board/, (ws) => {
    socket = ws
    ws.onMessage((data) => {
      for (const part of String(data).split('\u001e').filter(Boolean)) {
        const message = JSON.parse(part) as {
          type?: number
          target?: string
          invocationId?: string
        }
        if (message.type === undefined) ws.send(record({}))
        else if (message.type === 1 && message.target === 'Join') {
          ws.send(
            record({ type: 3, invocationId: message.invocationId, result: [me, bo].map(asUser) }),
          )
          joined = true
        }
      }
    })
  })

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const { pathname } = new URL(route.request().url())
      if (pathname === '/api/hubs/board/negotiate') {
        return route.fulfill({
          json: {
            negotiateVersion: 1,
            connectionId: 'conn-1',
            connectionToken: 'token-1',
            availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }],
          },
        })
      }
      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards') return route.fulfill({ json: [board] })
      if (pathname === '/api/boards/b1') return route.fulfill({ json: board })
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/members') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/lists') {
        return route.fulfill({
          json: lists.map((l) => ({ ...l, cards: card.listId === l.id ? [card] : [] })),
        })
      }
      if (pathname === '/api/boards/b1/cards/c1') {
        return route.fulfill({
          json: {
            card,
            description: null,
            createdById: me.id,
            createdByName: me.displayName,
            createdAt: me.createdAt,
            checklist: [],
            attachments: [],
            recurrence: null,
          },
        })
      }
      return route.fulfill({ status: 404 })
    },
  )

  return {
    joined: () => joined,
    push: (target: string, argument: unknown) =>
      socket!.send(record({ type: 1, target, arguments: [argument] })),
  }
}

function asUser(u: { id?: string; userId?: string; displayName: string; avatarColor: string }) {
  return { userId: u.userId ?? u.id, displayName: u.displayName, avatarColor: u.avatarColor }
}

const event = (type: string, extra: object = {}) => ({
  type,
  boardId: 'b1',
  cardId: 'c1',
  listId: null,
  position: null,
  userId: null,
  actorId: bo.userId,
  at: '2026-11-19T10:00:00Z',
  ...extra,
})

test('another member’s moves and edits show up live, with presence', async ({ page }) => {
  const hub = await mockApi(page)
  await page.goto('/boards/b1')

  await expect(page.getByText('2 people here')).toBeVisible()
  expect(hub.joined()).toBe(true)
  const todo = page.getByRole('list', { name: 'To do cards' })
  const doing = page.getByRole('list', { name: 'In progress cards' })
  await expect(todo.getByText('Write the spec')).toBeVisible()

  // Bo moves the card and renames it in their browser.
  Object.assign(card, { listId: 'l2', title: 'Spec written', rowVersion: 'v2' })
  hub.push('event', event('card.moved', { listId: 'l2', position: 1024 }))
  hub.push('event', event('card.updated'))

  await expect(doing.getByText('Spec written')).toBeVisible()
  await expect(todo.getByText('Write the spec')).toHaveCount(0)

  // Bo leaves the board.
  hub.push('presence', { boardId: 'b1', users: [asUser(me)] })
  await expect(page.getByText('2 people here')).toHaveCount(0)
})
