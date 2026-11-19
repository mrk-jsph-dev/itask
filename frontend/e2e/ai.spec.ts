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
  name: 'Launch',
  emoji: '🚀',
  description: null,
  color: '#1D5BF5',
  role: 'Owner',
  isStarred: false,
  memberCount: 1,
  archivedAt: null,
  keyPrefix: 'LA',
  rowVersion: 'AQ==',
}

const bug = { id: 'lb1', name: 'Bug', color: '#EF4444', emoji: null }

/** Mocks one board with AI on (or off) and records what the page sends. */
async function mockApi(page: Page, aiEnabled: boolean) {
  const cards: Record<string, unknown>[] = []
  const sent: { quickAdd?: unknown; created?: unknown; patch?: unknown; labels?: unknown } = {}

  const card = (title: string) => ({
    id: 'c1',
    listId: 'l3',
    number: 1,
    key: 'LA-1',
    title,
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
  })

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const { pathname } = new URL(request.url())
      const method = request.method()

      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/ai/status') {
        return route.fulfill({
          json: aiEnabled
            ? { enabled: true, limit: 20, remaining: 12 }
            : { enabled: false, limit: 0, remaining: 0 },
        })
      }
      if (pathname === '/api/boards' && method === 'GET') return route.fulfill({ json: [board] })
      if (pathname === '/api/boards/b1') return route.fulfill({ json: board })
      if (pathname === '/api/boards/b1/lists') {
        return route.fulfill({
          json: ['To do', 'In progress', 'Review', 'Done'].map((name, i) => ({
            id: `l${i + 1}`,
            name,
            emoji: null,
            position: (i + 1) * 1024,
            wipLimit: null,
            archivedAt: null,
            cards: i === 2 ? cards : [],
          })),
        })
      }
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [bug] })
      if (pathname === '/api/boards/b1/invites') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/members') {
        return route.fulfill({
          json: [{ ...me, userId: me.id, role: 'Owner', joinedAt: me.createdAt }],
        })
      }
      if (pathname === '/api/notifications/unread-count')
        return route.fulfill({ json: { count: 0 } })
      if (pathname === '/api/boards/b1/ai/quick-add') {
        sent.quickAdd = request.postDataJSON()
        return route.fulfill({
          json: {
            title: 'Fix login bug',
            dueDate: '2030-01-04',
            priority: 'High',
            labelIds: ['lb1'],
            listId: 'l3',
            remaining: 11,
          },
        })
      }
      if (pathname === '/api/boards/b1/lists/l2/cards' && method === 'POST') {
        sent.created = request.postDataJSON()
        const created = card((sent.created as { title: string }).title)
        cards.push(created)
        return route.fulfill({ status: 201, json: { ...created, listId: 'l2' } })
      }
      if (pathname === '/api/boards/b1/cards/c1' && method === 'PATCH') {
        sent.patch = request.postDataJSON()
        return route.fulfill({
          json: { ...card('Fix the login bug'), listId: 'l2', priority: 'High' },
        })
      }
      if (pathname === '/api/boards/b1/cards/c1/labels' && method === 'PUT') {
        sent.labels = request.postDataJSON()
        return route.fulfill({
          json: { ...card('Fix the login bug'), listId: 'l2', priority: 'High', labels: [bug] },
        })
      }
      return route.fulfill({ status: 404, json: { title: `No mock for ${method} ${pathname}` } })
    },
  )
  return sent
}

test('quick add drafts a task with AI, the user edits it and adds it', async ({ page }) => {
  const sent = await mockApi(page, true)

  await page.goto('/boards/b1')
  await page.getByRole('button', { name: 'AI helpers' }).click()
  const menu = page.getByRole('menu', { name: 'AI helpers' })
  await expect(menu).toContainText('12 of 20 AI requests left today')
  await menu.getByRole('menuitem', { name: /Quick add/ }).click()

  const dialog = page.getByRole('dialog', { name: '✨ Quick add' })
  await dialog.getByLabel('Describe the task').fill('Fix login bug by Friday, high, bug')
  await dialog.getByRole('button', { name: 'Draft task' }).click()
  expect(sent.quickAdd).toEqual({ text: 'Fix login bug by Friday, high, bug' })

  const draft = dialog.getByRole('form', { name: 'Task draft' })
  await expect(draft.getByLabel('Title')).toHaveValue('Fix login bug')
  await expect(draft.getByLabel('List')).toHaveValue('l3')
  await expect(draft.getByLabel('Priority')).toHaveValue('High')
  await expect(draft.getByLabel('Due')).toHaveValue('2030-01-04')
  await expect(draft.getByRole('button', { name: 'Bug' })).toHaveAttribute('aria-pressed', 'true')
  await expect(dialog).toContainText('11 of 20 AI requests left today')

  await draft.getByLabel('Title').fill('Fix the login bug')
  await draft.getByLabel('List').selectOption({ label: 'In progress' })
  await draft.getByRole('button', { name: 'Add task' }).click()

  await expect(dialog).toBeHidden()
  expect(sent.created).toEqual({ title: 'Fix the login bug' })
  expect(sent.patch).toMatchObject({ priority: 'High' })
  expect((sent.patch as { dueAt: string }).dueAt).toMatch(/^2030-01-0[45]T/)
  expect(sent.labels).toEqual({ labelIds: ['lb1'] })
  await expect(page.getByText('✨ Added “Fix the login bug”.')).toBeVisible()
})

test('with AI off the board shows no AI button', async ({ page }) => {
  await mockApi(page, false)

  await page.goto('/boards/b1')
  await expect(page.getByRole('heading', { name: 'To do' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'AI helpers' })).toHaveCount(0)
})
