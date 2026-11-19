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

const kanban = {
  id: 'kanban',
  source: 'builtIn',
  name: 'Kanban',
  emoji: '📋',
  description: 'A simple flow from idea to done.',
  color: '#1D5BF5',
  lists: ['Backlog', 'To do', 'In progress', 'Review', 'Done'],
  labelCount: 3,
  cardCount: 2,
}

const sprint = {
  ...kanban,
  id: 'sprint',
  name: 'Sprint',
  emoji: '🏃',
  color: '#7C3AED',
  lists: ['Product backlog', 'Sprint backlog', 'Done'],
}

/** Mocks Home, the template gallery and the board the Kanban template creates. */
async function mockApi(page: Page) {
  const boards: Record<string, unknown>[] = []
  let recurrence: Record<string, unknown> | null = null
  const sent: { template?: unknown; recurrence?: unknown } = {}

  const card = () => ({
    id: 'c1',
    listId: 'l2',
    number: 1,
    key: 'LA-1',
    title: 'Welcome to your Kanban board',
    emoji: '👋',
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
    checklistTotal: 3,
    commentCount: 0,
    attachmentCount: 0,
    recurring: recurrence !== null,
    rowVersion: 'v1',
  })

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const { pathname } = new URL(request.url())
      const method = request.method()
      const base = '/api/boards/b2/cards/c1'

      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards' && method === 'GET') return route.fulfill({ json: boards })
      if (pathname === '/api/templates') {
        return route.fulfill({ json: { builtIn: [kanban, sprint], mine: [] } })
      }
      if (pathname === '/api/templates/kanban/boards' && method === 'POST') {
        sent.template = request.postDataJSON()
        const body = sent.template as { name: string; emoji: string; color: string }
        const created = { id: 'b2', ...body, description: null, role: 'Owner' }
        boards.push({ ...created, isStarred: false, memberCount: 1, archivedAt: null })
        return route.fulfill({ status: 201, json: created })
      }
      if (pathname === '/api/boards/b2') {
        return route.fulfill({ json: { ...boards[0], keyPrefix: 'LA', rowVersion: 'AQ==' } })
      }
      if (pathname === '/api/boards/b2/lists') {
        return route.fulfill({
          json: kanban.lists.map((name, i) => ({
            id: `l${i + 1}`,
            name,
            emoji: null,
            position: (i + 1) * 1024,
            wipLimit: null,
            archivedAt: null,
            cards: i === 1 ? [card()] : [],
          })),
        })
      }
      if (pathname === '/api/boards/b2/labels' || pathname === '/api/boards/b2/invites')
        return route.fulfill({ json: [] })
      if (pathname === '/api/me/due-soon') {
        return route.fulfill({ json: { dueThisWeek: 0, overdue: 0, items: [] } })
      }
      if (pathname === '/api/notifications/unread-count')
        return route.fulfill({ json: { count: 0 } })
      if (pathname === '/api/boards/b2/members') {
        return route.fulfill({
          json: [{ ...me, userId: me.id, role: 'Owner', joinedAt: me.createdAt }],
        })
      }
      if (pathname === base && method === 'GET') {
        return route.fulfill({
          json: {
            card: card(),
            description: null,
            createdById: me.id,
            createdByName: me.displayName,
            createdAt: me.createdAt,
            checklist: [],
            attachments: [],
            recurrence,
          },
        })
      }
      if (pathname === `${base}/comments`) {
        return route.fulfill({ json: { items: [], nextCursor: null } })
      }
      if (pathname === `${base}/recurrence` && method === 'PUT') {
        sent.recurrence = request.postDataJSON()
        recurrence = {
          ...(sent.recurrence as object),
          nextRunAt: '2030-03-15T00:00:00Z',
        }
        return route.fulfill({ json: recurrence })
      }
      return route.fulfill({ status: 404, json: { title: `No mock for ${method} ${pathname}` } })
    },
  )
  return sent
}

test('a board starts from a template and a card is set to repeat monthly', async ({ page }) => {
  const sent = await mockApi(page)

  await page.goto('/')
  await page.getByRole('button', { name: '+ New board' }).first().click()
  const dialog = page.getByRole('dialog', { name: 'New board' })
  const templates = dialog.getByRole('radiogroup', { name: 'Templates' })
  await expect(templates.getByRole('radio', { name: /Blank board/ })).toBeChecked()

  await templates.getByRole('radio', { name: /Sprint/ }).click()
  await expect(dialog.getByLabel('Colour #7C3AED')).toBeChecked()
  await templates.getByRole('radio', { name: /Kanban/ }).click()
  await expect(dialog).toContainText('Backlog · To do · In progress · Review · Done')
  await dialog.getByLabel('Board name').fill('Launch')
  await dialog.getByRole('button', { name: 'Create board' }).click()

  await expect(page).toHaveURL(/\/boards\/b2$/)
  expect(sent.template).toMatchObject({ name: 'Launch', emoji: '📋', color: '#1D5BF5' })
  await expect(page.getByRole('heading', { name: 'Backlog' })).toBeVisible()

  await page.getByText('Welcome to your Kanban board').click()
  await expect(page).toHaveURL(/\/boards\/b2\/cards\/c1$/)
  const sheet = page.getByRole('dialog', { name: 'LA-1' })
  await sheet.getByRole('button', { name: /Doesn't repeat/ }).click()
  const picker = sheet.getByRole('dialog', { name: 'Repeat task' })
  await picker.getByRole('radio', { name: 'Monthly' }).click()
  await picker.getByLabel('Day of the month').fill('15')
  await picker.getByLabel('New copy goes to').selectOption({ label: 'To do' })
  await picker.getByRole('button', { name: 'Save' }).click()

  expect(sent.recurrence).toEqual({
    kind: 'Monthly',
    weekdays: [],
    dayOfMonth: 15,
    targetListId: 'l2',
  })
  await expect(sheet.getByRole('button', { name: /Every month on the 15th/ })).toBeVisible()
  await expect(picker).toBeHidden()
  await expect(page.getByRole('img', { name: 'Repeats' })).toBeVisible()
})
