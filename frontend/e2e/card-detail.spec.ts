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

const grace = {
  userId: 'u2',
  displayName: 'Grace Hopper',
  email: 'grace@example.com',
  avatarColor: '#0F766E',
}

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
}

interface Item {
  id: string
  text: string
  isDone: boolean
  position: number
  dueAt: string | null
  assigneeId: string | null
}

/** Mocks one board with one card, keeping checklist, comments and files server-side. */
async function mockApi(page: Page, role: 'Editor' | 'Viewer' = 'Editor') {
  const checklist: Item[] = []
  const comments: Record<string, unknown>[] = []
  const attachments: Record<string, unknown>[] = []
  let uploaded: { name: string; type: string } | null = null
  let next = 1

  const card = () => ({
    id: 'c1',
    listId: 'l1',
    number: 1,
    key: 'RM-1',
    title: 'Write the spec',
    position: 1024,
    dueAt: null,
    startAt: null,
    priority: 'None',
    coverColor: null,
    completedAt: null,
    archivedAt: null,
    labels: [],
    assignees: [],
    checklistDone: checklist.filter((i) => i.isDone).length,
    checklistTotal: checklist.length,
    commentCount: comments.length,
    emoji: null,
    attachmentCount: attachments.length,
    recurring: false,
    blocked: false,
    blockerCount: 0,
    blockingCount: 0,
    rowVersion: 'v1',
  })

  // Storage itself: the signed upload URL the API hands out.
  await page.route('https://storage.test/**', async (route) => {
    const request = route.request()
    expect(request.method()).toBe('PUT')
    expect(request.headers()['x-ms-blob-type']).toBe('BlockBlob')
    uploaded = { name: 'boards/b1/cards/c1/f1', type: request.headers()['content-type']! }
    return route.fulfill({ status: 201 })
  })

  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const request = route.request()
      const { pathname } = new URL(request.url())
      const method = request.method()
      const base = '/api/boards/b1/cards/c1'

      if (pathname === '/api/me') return route.fulfill({ json: me })
      if (pathname === '/api/boards/b1') return route.fulfill({ json: { ...board, role } })
      if (pathname === '/api/boards/b1/labels') return route.fulfill({ json: [] })
      if (pathname === '/api/boards/b1/members') {
        return route.fulfill({
          json: [
            { ...me, userId: me.id, role, joinedAt: me.createdAt },
            { ...grace, role: 'Editor', joinedAt: me.createdAt },
          ],
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
              cards: [card()],
            },
          ],
        })
      }
      if (pathname === base && method === 'GET') {
        return route.fulfill({
          json: {
            card: card(),
            description: '**Bold** plan',
            createdById: me.id,
            createdByName: me.displayName,
            createdAt: me.createdAt,
            checklist,
            attachments,
          },
        })
      }
      if (pathname === `${base}/checklist` && method === 'POST') {
        const { text } = request.postDataJSON() as { text: string }
        const item = {
          id: `i${next++}`,
          text,
          isDone: false,
          position: next * 1024,
          dueAt: null,
          assigneeId: null,
        }
        checklist.push(item)
        return route.fulfill({ status: 201, json: item })
      }
      const done = pathname.match(/\/checklist\/(\w+)\/done$/)
      if (done) {
        const item = checklist.find((i) => i.id === done[1])!
        item.isDone = (request.postDataJSON() as { isDone: boolean }).isDone
        return route.fulfill({ json: item })
      }
      if (pathname === `${base}/comments` && method === 'GET') {
        return route.fulfill({ json: { items: [...comments].reverse(), nextCursor: null } })
      }
      if (pathname === `${base}/comments` && method === 'POST') {
        const { body } = request.postDataJSON() as { body: string }
        const comment = {
          id: `m${next++}`,
          authorId: me.id,
          authorName: me.displayName,
          authorColor: me.avatarColor,
          body,
          createdAt: new Date().toISOString(),
          editedAt: null,
          mentions: body.includes('@Grace Hopper')
            ? [{ userId: grace.userId, displayName: grace.displayName }]
            : [],
        }
        comments.push(comment)
        return route.fulfill({ status: 201, json: comment })
      }
      if (pathname === `${base}/attachments/upload-url`) {
        return route.fulfill({
          json: {
            blobName: 'boards/b1/cards/c1/f1',
            uploadUrl: 'https://storage.test/attachments/f1?sig=x',
            expiresAt: new Date(Date.now() + 600_000).toISOString(),
          },
        })
      }
      if (pathname === `${base}/attachments` && method === 'POST') {
        const { blobName, fileName } = request.postDataJSON() as {
          blobName: string
          fileName: string
        }
        if (uploaded?.name !== blobName) return route.fulfill({ status: 400, json: {} })
        const attachment = {
          id: 'a1',
          fileName,
          contentType: uploaded.type,
          sizeBytes: 2048,
          uploadedById: me.id,
          uploadedByName: me.displayName,
          createdAt: new Date().toISOString(),
        }
        attachments.push(attachment)
        return route.fulfill({ status: 201, json: attachment })
      }
      return route.fulfill({ status: 404, json: { title: `No mock for ${method} ${pathname}` } })
    },
  )
}

test('editor fills a card with a checklist, a comment and a file', async ({ page }) => {
  await mockApi(page)
  await page.goto('/boards/b1/cards/c1')

  const sheet = page.getByRole('dialog', { name: 'RM-1' })
  await expect(sheet.getByRole('textbox', { name: '📝 Description' })).toHaveValue('**Bold** plan')
  await sheet.getByRole('tab', { name: 'Preview' }).click()
  await expect(sheet.locator('.md-body strong')).toHaveText('Bold')

  await sheet.getByRole('textbox', { name: 'New subtask' }).fill('Draft the API')
  await sheet.getByRole('button', { name: 'Add', exact: true }).click()
  await sheet.getByRole('checkbox', { name: 'Check Draft the API' }).check()
  await expect(sheet.getByRole('progressbar', { name: 'Checklist progress' })).toHaveAttribute(
    'aria-valuenow',
    '100',
  )
  await expect(page.getByRole('article', { name: 'RM-1 Write the spec' })).toContainText('1/1')

  const composer = sheet.getByRole('textbox', { name: 'Write a comment' })
  await composer.fill('Over to @gr')
  await sheet.getByRole('option', { name: '@Grace Hopper' }).click()
  await composer.pressSequentially('please review')
  await sheet.getByRole('button', { name: 'Send' }).click()
  await expect(sheet.locator('.mention')).toHaveText('@Grace Hopper')
  await expect(
    page.getByRole('article', { name: 'RM-1 Write the spec' }).getByLabel('1 comment'),
  ).toBeVisible()

  await sheet.getByLabel('Upload files').setInputFiles({
    name: 'plan.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.4'),
  })
  await expect(sheet.getByRole('button', { name: 'Download plan.pdf' })).toBeVisible()
  await expect(sheet.getByRole('list', { name: 'Attachments' })).toContainText('2 KB')

  await sheet.getByRole('button', { name: 'Close' }).click()
  await expect(page).toHaveURL(/\/boards\/b1$/)
})

test('viewer can comment but not change the checklist or upload', async ({ page }) => {
  await mockApi(page, 'Viewer')
  await page.goto('/boards/b1/cards/c1')

  const sheet = page.getByRole('dialog', { name: 'RM-1' })
  await expect(sheet.getByRole('heading', { name: /Subtasks/ })).toBeVisible()
  await expect(sheet.getByRole('textbox', { name: 'New subtask' })).toHaveCount(0)
  await expect(sheet.getByRole('button', { name: /Upload file/ })).toHaveCount(0)

  await sheet.getByRole('textbox', { name: 'Write a comment' }).fill('Looks good')
  await sheet.getByRole('button', { name: 'Send' }).click()
  await expect(sheet.getByRole('list', { name: 'Comments' })).toContainText('Looks good')
})
