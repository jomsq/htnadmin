import { expect, type Page } from '@playwright/test'

/**
 * Dev seed data (backend/src/Htn.Api/Seeding/DevSeeder.cs). It exists only when the API runs with
 * ASPNETCORE_ENVIRONMENT=Development and Seed__Dev=true, which is what `docker compose up` does by default.
 * The dev seed password is published in that file. It is not a secret and never exists outside Development.
 * Override with E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD if you seed different accounts.
 */
export const seed = {
  admin: {
    email: process.env.E2E_ADMIN_EMAIL || 'alex@hackthenorth.ph',
    password: process.env.E2E_ADMIN_PASSWORD || 'Dev-Password-123!',
  },
  event: { name: 'HTN Con 2026', slug: 'htn-con-2026' },
  studentTicket: { name: 'Student', price: 800 },
  /** 20% off the Student ticket only. Expires 2026-11-15 23:59 Manila time; the Student ticket stops selling 2026-11-20. */
  studentVoucher: { code: 'STUDENT20', percent: 20 },
} as const

/** HTN-XXXX-XXXX from PublicIdGenerator.Alphabet (no 0, 1, I, L, O, U). */
export const PARTICIPANT_ID_RE = /^HTN-[2-9A-HJKMNP-TV-Z]{4}-[2-9A-HJKMNP-TV-Z]{4}$/

/**
 * Board sign-in with the dev seed account. Needs Auth__RequireBoard2fa=false (appsettings.Development.json sets it),
 * otherwise a board user is sent to /setup-2fa and this fails on purpose.
 */
export async function signInAsBoard(page: Page) {
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Sign in to HTN Admin' })).toBeVisible()
  await page.getByLabel('Email').fill(seed.admin.email)
  await page.getByLabel('Password', { exact: true }).fill(seed.admin.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('heading', { name: 'Dashboard' }), 'board sign-in should skip 2FA setup (Auth__RequireBoard2fa=false)').toBeVisible()
}

/** Follows a main-navigation link; on phones the sidebar sits behind the "Open menu" button. */
export async function openMainNav(page: Page, label: string) {
  const menu = page.getByRole('button', { name: 'Open menu' })
  if (await menu.isVisible()) await menu.click()
  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: label, exact: true }).click()
}

/** Letters-only suffix (registration names allow letters only) that differs per run, project and retry. */
export function uniqueLetters(): string {
  const digits = `${Date.now()}`.slice(-8) + `${Math.floor(Math.random() * 1e4)}`.padStart(4, '0')
  return Array.from(digits, (d) => 'abcdefghij'[Number(d)]).join('')
}

export type CheckInEvent = { id: string; name: string; slug: string }

/**
 * Creates and publishes a throwaway event that starts in an hour, with a Student ticket (800) and a STUDENT20 voucher
 * mirroring the seeded HTN Con 2026. Check-in is only open from a day before the start to a day after the end, so
 * the seeded event (2026-11-22) can't be relied on; this keeps the flow independent of the calendar date.
 * Uses the signed-in board session of `page` (cookies + antiforgery token), i.e. the same Events.Manage API as the UI.
 */
export async function createCheckInEvent(page: Page, suffix: string): Promise<CheckInEvent> {
  const api = page.request
  const csrf = await api.get('/api/auth/antiforgery', { headers: { Accept: 'application/json' } })
  expect(csrf.ok(), `GET /api/auth/antiforgery -> ${csrf.status()}`).toBe(true)
  const { token } = (await csrf.json()) as { token: string }
  const headers = { 'X-CSRF-TOKEN': token, Accept: 'application/json' }

  async function post(path: string, data?: unknown) {
    const res = await api.post(path, { headers, data })
    expect(res.ok(), `POST ${path} -> ${res.status()} ${await res.text()}`).toBe(true)
    return res
  }

  const startsAt = new Date(Date.now() + 60 * 60 * 1000)
  const endsAt = new Date(startsAt.getTime() + 8 * 60 * 60 * 1000)
  const name = `E2E Check-in ${suffix}`
  const slug = `e2e-checkin-${suffix}`
  const created = (await (
    await post('/api/events', {
      name,
      slug,
      startsAt: startsAt.toISOString(),
      endsAt: endsAt.toISOString(),
      venue: 'Playwright Hall',
      capacity: 50,
      description: 'Created by the Playwright e2e suite.',
    })
  ).json()) as { id: string }

  await post(`/api/events/${created.id}/tickets`, {
    name: seed.studentTicket.name,
    price: seed.studentTicket.price,
    quantity: 10,
    groupSize: 1,
    visibility: 'Public',
  })
  const detail = await api.get(`/api/events/${created.id}`, { headers })
  expect(detail.ok(), `GET /api/events/${created.id} -> ${detail.status()}`).toBe(true)
  const { tickets } = (await detail.json()) as { tickets: { id: string; name: string }[] }
  const student = tickets.find((t) => t.name === seed.studentTicket.name)
  expect(student, 'the Student ticket should exist after creating it').toBeDefined()

  await post(`/api/events/${created.id}/vouchers`, {
    code: seed.studentVoucher.code,
    kind: 'Percent',
    value: seed.studentVoucher.percent,
    ticketTypeIds: [student?.id],
    usageLimit: 5,
  })
  await post(`/api/events/${created.id}/publish`)
  return { id: created.id, name, slug }
}
