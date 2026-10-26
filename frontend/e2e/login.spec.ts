import { expect, test } from '@playwright/test'

// Requires the full stack (docker compose up) with the dev seed admin.
const email = process.env.E2E_ADMIN_EMAIL ?? 'alex@hackthenorth.ph'
const password = process.env.E2E_ADMIN_PASSWORD ?? ''

test('admin signs in and lands on the dashboard', async ({ page }) => {
  test.skip(!password, 'Set E2E_ADMIN_PASSWORD to run against a seeded stack')
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Sign in to HTN Admin' })).toBeVisible()
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
})

test('sign-in page has no public sign-up', async ({ page }) => {
  await page.goto('/login')
  await expect(page.getByText('No account? Accounts are created by the board.')).toBeVisible()
  await expect(page.getByRole('link', { name: /sign up/i })).toHaveCount(0)
})
