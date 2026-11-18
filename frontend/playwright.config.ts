import process from 'node:process'
import { defineConfig, devices } from '@playwright/test'

/** https://playwright.dev/docs/test-configuration */
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  expect: { timeout: 5_000 },
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    baseURL: process.env.CI ? 'http://localhost:4173' : 'http://localhost:5173',
    trace: 'retain-on-failure',
    headless: true,
  },
  projects: [
    {
      name: 'chromium',
      // CI runners ship Google Chrome, so CI skips downloading Playwright's Chromium.
      use: { ...devices['Desktop Chrome'], channel: process.env.CI ? 'chrome' : undefined },
    },
  ],
  webServer: {
    command: process.env.CI ? 'npm run preview' : 'npm run dev',
    port: process.env.CI ? 4173 : 5173,
    reuseExistingServer: !process.env.CI,
  },
})
