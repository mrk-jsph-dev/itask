import { test, expect } from '@playwright/test'

test('signed-out visitors land on the sign-in page', async ({ page }) => {
  await page.route('**/api/me', (route) => route.fulfill({ status: 401 }))
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible()
})
