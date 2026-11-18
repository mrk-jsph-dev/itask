import { test, expect, type Page } from '@playwright/test'

const user = {
  id: 'u1',
  email: 'ada@example.com',
  displayName: 'Ada',
  avatarColor: '#1D5BF5',
  timeZone: 'UTC',
  isPlatformAdmin: false,
  isDemo: false,
  createdAt: '2026-11-18T00:00:00Z',
}

async function signInAs(page: Page, isPlatformAdmin: boolean) {
  await page.route('**/api/me', (route) => route.fulfill({ json: { ...user, isPlatformAdmin } }))
  await page.route('**/api/boards', (route) => route.fulfill({ json: [] }))
}

test('regular users have no admin link and are sent home', async ({ page }) => {
  await signInAs(page, false)

  await page.goto('/admin/users')

  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByRole('link', { name: /Admin/ })).toBeHidden()
})

test('platform admins see and lock users', async ({ page }) => {
  await signInAs(page, true)
  let locked = false
  await page.route('**/api/admin/users', (route) =>
    route.fulfill({
      json: {
        items: [
          { ...user, isPlatformAdmin: true, isLocked: false },
          { ...user, id: 'u2', email: 'bob@example.com', displayName: 'Bob', isLocked: locked },
        ],
        nextCursor: null,
      },
    }),
  )
  await page.route('**/api/admin/users/u2/lock', async (route) => {
    locked = (route.request().postDataJSON() as { locked: boolean }).locked
    await route.fulfill({ status: 204 })
  })

  await page.goto('/')
  await page.getByRole('link', { name: /Admin/ }).click()

  await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Lock ada@example.com' })).toBeHidden()
  await page.getByRole('button', { name: 'Lock bob@example.com' }).click()
  await expect(page.getByText('Locked', { exact: true })).toBeVisible()
  expect(locked).toBe(true)
})
