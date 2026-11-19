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

test('a visitor creates an account, enters the emailed code and lands on their boards', async ({
  page,
}) => {
  let signedIn = false
  let registered: unknown
  const verified: unknown[] = []
  await page.route('**/api/me', (route) =>
    signedIn ? route.fulfill({ json: me }) : route.fulfill({ status: 401 }),
  )
  await page.route('**/api/auth/register', async (route) => {
    registered = route.request().postDataJSON()
    await route.fulfill({ json: { email: me.email } })
  })
  await page.route('**/api/auth/verify-email', async (route) => {
    const body = route.request().postDataJSON() as { email: string; code: string }
    verified.push(body)
    if (body.code !== '482913') {
      return route.fulfill({ status: 400, json: { title: 'That code is wrong or has expired.' } })
    }
    signedIn = true
    return route.fulfill({ status: 200, body: '' })
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

  expect(registered).toEqual({
    displayName: 'Ada Lovelace',
    email: me.email,
    password: 'Abcdefgh1!',
  })
  await expect(page).toHaveURL(/\/verify-email\?email=/)
  await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible()
  await expect(page.getByText('a**@example.com')).toBeVisible()
  await expect(page.getByRole('button', { name: /Send a new code in \d+s/ })).toBeDisabled()

  const code = page.getByLabel('Code')
  await code.fill('111 111')
  await page.getByRole('button', { name: 'Verify and continue' }).click()
  await expect(page.getByRole('alert')).toHaveText('That code is wrong or has expired.')

  await code.fill('482-913')
  await page.getByRole('button', { name: 'Verify and continue' }).click()

  await expect(page.getByRole('heading', { name: 'Your boards' })).toBeVisible()
  expect(verified.at(-1)).toEqual({ email: me.email, code: '482913' })
})
