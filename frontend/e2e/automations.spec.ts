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
  role: 'Owner',
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

const baseCard = {
  id: 'c1',
  listId: 'l1',
  number: 1,
  key: 'RM-1',
  title: 'Pick fonts',
  emoji: null,
  position: 1024,
  dueAt: null,
  startAt: null,
  priority: 'None',
  coverColor: null,
  completedAt: null as string | null,
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

const automation = (extra: Record<string, unknown>) => ({
  listId: null,
  direction: null,
  mode: null,
  trigger: { type: 'card.completed' },
  conditions: [],
  actions: [],
  enabled: true,
  timing: 'instant',
  runs: 0,
  blocked: 0,
  lastRunAt: null,
  problem: null,
  createdAt: '2026-11-19T00:00:00Z',
  rowVersion: 'r1',
  ...extra,
})

const runsHeader = (runs: unknown[]) => Buffer.from(JSON.stringify(runs), 'utf8').toString('base64')

/** Mocks a board whose Done list has a block-mode doorway that needs an assignee. */
async function mockApi(page: Page) {
  const card = { ...baseCard }
  const automations = [
    automation({
      id: 'a1',
      kind: 'Doorway',
      name: 'Done gate',
      listId: 'l3',
      direction: 'Enter',
      mode: 'Block',
      trigger: { type: 'card.entered', listId: 'l3' },
      conditions: [{ type: 'assignee.set' }],
    }),
  ]
  const calls = { overridden: false, undone: '', recordedCard: '' }

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const { pathname } = new URL(request.url())
      const method = request.method()

      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards/b1') return route.fulfill({ json: board })
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/invites') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/members') {
        return route.fulfill({
          json: [{ ...me, userId: me.id, role: 'Owner', joinedAt: me.createdAt }],
        })
      }
      if (pathname === '/api/boards/b1/lists') {
        return route.fulfill({
          json: lists.map((l) => ({ ...l, cards: card.listId === l.id ? [card] : [] })),
        })
      }
      if (pathname === '/api/boards/b1/automations/summary') {
        return route.fulfill({
          json: {
            days: 7,
            ran: 3,
            blocked: 1,
            sentence: 'Automations made 3 changes; doorways blocked 1 move.',
            rules: [],
          },
        })
      }
      if (pathname === '/api/boards/b1/automations/from-recording') {
        calls.recordedCard = (request.postDataJSON() as { cardId: string }).cardId
        return route.fulfill({
          json: {
            name: 'In progress routine',
            trigger: { type: 'card.entered', listId: 'l2' },
            actions: [{ type: 'priority', priority: 'High' }],
          },
        })
      }
      if (pathname === '/api/boards/b1/automations/preview') {
        return route.fulfill({
          json: {
            count: 3,
            samples: [{ cardId: 'c1', key: 'RM-1', title: 'Pick fonts' }],
            basis: 'history',
          },
        })
      }
      if (pathname === '/api/boards/b1/automations') {
        if (method === 'POST') {
          const body = request.postDataJSON() as Record<string, unknown>
          const saved = automation({ ...body, id: `a${automations.length + 1}` })
          automations.push(saved)
          return route.fulfill({ status: 201, json: saved })
        }
        return route.fulfill({ json: automations })
      }
      if (pathname === '/api/boards/b1/cards/c1/position') {
        const body = request.postDataJSON() as { listId: string; overrideDoorway?: boolean }
        if (body.listId === 'l3' && !body.overrideDoorway) {
          return route.fulfill({
            status: 409,
            json: {
              title: 'Done needs an assignee first.',
              status: 409,
              code: 'doorway',
              rule: 'Done gate',
              unmet: ['an assignee'],
            },
          })
        }
        calls.overridden = !!body.overrideDoorway
        card.listId = body.listId
        card.completedAt = '2026-11-19T12:00:00Z'
        card.rowVersion = 'v2'
        return route.fulfill({
          json: { card, renumbered: false },
          headers: {
            'X-ITask-Automations': runsHeader([
              {
                runId: 'run1',
                ruleName: 'Done completes',
                summary: 'Marked complete',
                canUndo: true,
              },
            ]),
          },
        })
      }
      const undo = pathname.match(/^\/api\/boards\/b1\/automation-runs\/(\w+)\/undo$/)
      if (undo) {
        calls.undone = undo[1]!
        card.completedAt = null
        return route.fulfill({ json: card })
      }
      return route.fulfill({ status: 404 })
    },
  )
  return calls
}

test('owner adds a preset, a doorway blocks a move, and an automation run is undone', async ({
  page,
}) => {
  const calls = await mockApi(page)
  await page.goto('/boards/b1')

  await expect(page.getByRole('button', { name: 'Done doorways' })).toBeVisible()

  await page.getByRole('button', { name: 'Automations' }).click()
  const sheet = page.getByRole('dialog', { name: '⚡ Automations' })
  await expect(
    sheet.getByText('Coming into Done needs an assignee. Otherwise: blocked.'),
  ).toBeVisible()
  await expect(sheet.getByText('Instant · free').first()).toBeVisible()
  await sheet.getByRole('button', { name: 'Add Done completes' }).click()
  await expect(sheet.getByText('When a card enters Done, mark it complete.')).toBeVisible()
  await page.keyboard.press('Escape')

  await page.getByRole('button', { name: 'Pick fonts card menu' }).click()
  await page.getByRole('menuitem', { name: /Move to list/ }).click()
  await page.getByRole('menuitem', { name: /Done/ }).click()

  const refusal = page.getByRole('alertdialog')
  await expect(refusal).toContainText('Done needs an assignee first.')
  await expect(refusal).toContainText('Add an assignee')
  await refusal.getByRole('button', { name: 'Move anyway' }).click()

  await expect(page.getByText('⚡ Marked complete · Done completes')).toBeVisible()
  expect(calls.overridden).toBe(true)
  await page.getByRole('button', { name: 'Undo' }).click()
  await expect.poll(() => calls.undone).toBe('run1')
})

test('show me once turns recorded steps into a rule, replayed before it is added', async ({
  page,
}) => {
  const calls = await mockApi(page)
  await page.goto('/boards/b1')

  await page.getByRole('button', { name: 'Automations' }).click()
  const sheet = page.getByRole('dialog', { name: '⚡ Automations' })
  await expect(sheet.getByTestId('automation-summary')).toContainText(
    'Automations made 3 changes; doorways blocked 1 move.',
  )
  await sheet.getByRole('button', { name: '⏺ Show me once' }).click()

  await expect(page.getByRole('status').filter({ hasText: 'Recording' })).toBeVisible()
  await page.getByRole('button', { name: 'Pick fonts card menu' }).click()
  await page.getByRole('menuitem', { name: /Move to list/ }).click()
  await page.getByRole('menuitem', { name: /In progress/ }).click()
  await expect(page.getByText('Do the rest of the steps')).toBeVisible()
  const drafted = page.waitForResponse((r) => r.url().endsWith('/automations/from-recording'))
  await page.getByRole('button', { name: '⏹ Stop' }).click()
  await drafted

  const editor = page.getByRole('dialog', { name: '⚡ Automations' })
  await expect(editor.getByLabel('Name')).toHaveValue('In progress routine')
  expect(calls.recordedCard).toBe('c1')
  await editor.getByRole('button', { name: '⏪ Replay before adding' }).click()
  await expect(editor.getByTestId('automation-preview')).toContainText(
    'Would have run 3 times in the last 30 days',
  )
  await editor.getByRole('button', { name: 'Save' }).click()
  await expect(editor.getByText('When a card enters In progress, set priority High.')).toBeVisible()
})
