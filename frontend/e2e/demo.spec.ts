import { test, expect } from '@playwright/test'

const editor = {
  id: '2',
  email: 'demo-editor@example.com',
  displayName: 'Demo Editor',
  avatarColor: '#0F766E',
  timeZone: 'UTC',
  isPlatformAdmin: false,
  isDemo: true,
  hasPassword: true,
  createdAt: '2026-11-18T00:00:00Z',
}

test('demo buttons sign in as the chosen role', async ({ page }) => {
  let signedIn = false
  await page.route('**/api/me', (route) =>
    signedIn ? route.fulfill({ json: editor }) : route.fulfill({ status: 401 }),
  )
  await page.route('**/api/auth/demo', (route) => route.fulfill({ json: { enabled: true } }))
  await page.route('**/api/auth/demo/editor', async (route) => {
    signedIn = true
    await route.fulfill({ status: 204 })
  })
  await page.route('**/api/boards', (route) => route.fulfill({ json: [] }))

  await page.goto('/sign-in')
  await expect(page.getByRole('heading', { name: 'Try a demo' })).toBeVisible()
  await page.getByRole('button', { name: 'Sign in as demo Editor' }).click()

  await expect(page.getByRole('heading', { name: 'Your boards' })).toBeVisible()
  await page
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name: 'Profile' })
    .click()
  await expect(page.getByText("Demo accounts can't change their password.")).toBeVisible()
})

test('demo buttons stay hidden when demo mode is off', async ({ page }) => {
  await page.route('**/api/me', (route) => route.fulfill({ status: 401 }))
  await page.route('**/api/auth/demo', (route) => route.fulfill({ json: { enabled: false } }))

  await page.goto('/sign-in')

  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Try a demo' })).toBeHidden()
})
