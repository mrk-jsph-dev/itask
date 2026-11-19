import { test, expect } from '@playwright/test'

const me = {
  id: '1',
  email: 'ada@example.com',
  displayName: 'Ada Lovelace',
  avatarColor: '#1D5BF5',
  timeZone: 'UTC',
  isPlatformAdmin: false,
  isDemo: false,
  hasPassword: true,
  createdAt: '2026-11-18T00:00:00Z',
}

test('a visitor creates an account and lands on their boards', async ({ page }) => {
  let signedIn = false
  let registered: unknown
  await page.route('**/api/me', (route) =>
    signedIn ? route.fulfill({ json: me }) : route.fulfill({ status: 401 }),
  )
  await page.route('**/api/auth/register', async (route) => {
    registered = route.request().postDataJSON()
    await route.fulfill({ status: 200, body: '' })
  })
  await page.route('**/api/auth/login*', async (route) => {
    signedIn = true
    await route.fulfill({ status: 200, body: '' })
  })
  await page.route('**/api/boards', (route) => route.fulfill({ json: [] }))

  await page.goto('/register')
  await expect(page.getByRole('heading', { name: 'Create your account' })).toBeVisible()

  await page.getByLabel('Full name').fill('Ada Lovelace')
  await page.getByLabel('Email').fill(me.email)
  const create = page.getByRole('button', { name: 'Create account' })

  await page.getByLabel('Password', { exact: true }).fill('abcdefghij')
  await expect(page.getByRole('meter', { name: 'Password strength' })).toHaveAttribute(
    'aria-valuenow',
    '1',
  )
  await expect(create).toBeDisabled()

  await page.getByLabel('Password', { exact: true }).fill('Abcdefgh1!')
  await expect(page.getByRole('meter', { name: 'Password strength' })).toHaveAttribute(
    'aria-valuenow',
    '4',
  )
  await create.click()

  await expect(page.getByRole('heading', { name: 'Your boards' })).toBeVisible()
  expect(registered).toEqual({
    displayName: 'Ada Lovelace',
    email: me.email,
    password: 'Abcdefgh1!',
  })
})
