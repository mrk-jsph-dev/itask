import { test, expect } from '@playwright/test'

test('signed-out visitors land on the sign-in page', async ({ page }) => {
  await page.route('**/api/me', (route) => route.fulfill({ status: 401 }))
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible()
})

test('a locked account gets a clear message', async ({ page }) => {
  await page.route('**/api/me', (route) => route.fulfill({ status: 401 }))
  await page.route('**/api/auth/demo', (route) => route.fulfill({ json: { enabled: false } }))
  await page.route('**/api/auth/login*', (route) =>
    route.fulfill({ status: 401, json: { title: 'Unauthorized', status: 401, detail: 'Lockedout' } }),
  )

  await page.goto('/sign-in')
  await page.getByLabel('Email').fill('someone@example.com')
  await page.getByLabel('Password').fill('Wrong-password-1')
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()

  await expect(page.getByRole('alert')).toHaveText(/locked for a few minutes/)
})
