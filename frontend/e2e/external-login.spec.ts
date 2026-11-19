import { test, expect } from '@playwright/test'

const me = {
  id: '1',
  email: 'ada@example.com',
  displayName: 'Ada Lovelace',
  avatarColor: '#1D5BF5',
  timeZone: 'UTC',
  isPlatformAdmin: false,
  isDemo: false,
  hasPassword: false,
  createdAt: '2026-11-18T00:00:00Z',
}

test('the sign-in page offers the configured providers', async ({ page }) => {
  await page.route('**/api/me', (route) => route.fulfill({ status: 401 }))
  await page.route('**/api/auth/demo', (route) => route.fulfill({ json: { enabled: false } }))
  await page.route('**/api/auth/providers', (route) =>
    route.fulfill({ json: { providers: ['github', 'microsoft'] } }),
  )

  await page.goto('/sign-in')

  await expect(page.getByRole('button', { name: 'Continue with GitHub' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Continue with Microsoft' })).toBeVisible()
})

test('returning from GitHub redeems the code with the tab nonce and opens the next page', async ({
  page,
}) => {
  let signedIn = false
  let completed: unknown
  await page.addInitScript(() => {
    sessionStorage.setItem('itask.external.nonce', 'tab-nonce')
    sessionStorage.setItem('itask.external.next', '/activity')
  })
  await page.route('**/api/me', (route) =>
    signedIn ? route.fulfill({ json: me }) : route.fulfill({ status: 401 }),
  )
  await page.route('**/api/auth/external/complete', async (route) => {
    completed = route.request().postDataJSON()
    signedIn = true
    await route.fulfill({ json: { mode: 'signIn' } })
  })
  await page.route('**/api/me/activity*', (route) =>
    route.fulfill({ json: { items: [], nextCursor: null } }),
  )
  await page.route('**/api/boards', (route) => route.fulfill({ json: [] }))

  await page.goto('/auth/external?code=one-time')

  await expect(page).toHaveURL(/\/activity$/)
  expect(completed).toEqual({ code: 'one-time', nonce: 'tab-nonce' })
})

test('a callback error is explained', async ({ page }) => {
  await page.route('**/api/me', (route) => route.fulfill({ status: 401 }))

  await page.goto('/auth/external?error=account_exists')

  await expect(page.getByRole('alert')).toHaveText(/Sign in with your password/)
  await expect(page).toHaveURL(/\/auth\/external$/)
  await expect(page.getByRole('link', { name: 'Back to sign in' })).toBeVisible()
})
