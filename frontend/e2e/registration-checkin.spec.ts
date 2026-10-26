/**
 * Critical Phase 3 flow: public registration -> mark paid -> desk check-in (CLAUDE.md non-negotiable 9).
 *
 * Runs against the full stack, never against mocks:
 *   cp .env.example .env && docker compose up -d --build     # web :5173, api :8080, db :5432
 *   cd frontend && npx playwright test e2e/registration-checkin.spec.ts
 *
 * Required API environment (all are the docker compose defaults):
 *   ASPNETCORE_ENVIRONMENT=Development   dev seed + appsettings.Development.json
 *   Seed__Dev=true (SEED_DEV)            seeds alex@hackthenorth.ph (Board) and HTN Con 2026 (Student ticket, STUDENT20)
 *   Auth__RequireBoard2fa=false          board users sign in without TOTP; Development already sets this
 *   MAILTRAP_API_TOKEN unset             emails go to the logging sender; nothing is sent for real
 *
 * Test-runner environment (optional):
 *   E2E_BASE_URL         default http://localhost:5173
 *   E2E_ADMIN_EMAIL      default alex@hackthenorth.ph (dev seed)
 *   E2E_ADMIN_PASSWORD   default the published dev seed password
 *
 * Dates: check-in only opens a day before an event starts and closes a day after it ends, and the seeded
 * HTN Con 2026 is on 2026-11-22 (STUDENT20 there expires 2026-11-15). So after checking the seed is in place, the test
 * creates its own published event starting in an hour, with the same Student ticket (800) and STUDENT20 (20%), and runs
 * the flow on it. A participant ID only matches within its own event, so registration and check-in share that event.
 *
 * The ticket QR token is never exposed by the API or this test. Check-in uses the desk's typed participant ID lookup:
 * typing an ID shows a "Check the ticket holder" card (result Lookup) and staff confirm with its "Check in" button.
 */
import { expect, test, type BrowserContextOptions } from '@playwright/test'
import { createCheckInEvent, openMainNav, PARTICIPANT_ID_RE, seed, signInAsBoard, uniqueLetters, type CheckInEvent } from './support/seed'

test('visitor registers with STUDENT20, admin marks paid, desk checks in once', async ({ page: admin, browser, baseURL }, testInfo) => {
  const { viewport, userAgent, deviceScaleFactor, isMobile, hasTouch } = testInfo.project.use
  const deviceOptions: BrowserContextOptions = { baseURL, viewport, userAgent, deviceScaleFactor, isMobile, hasTouch }

  const suffix = uniqueLetters()
  const visitor = { firstName: 'Eetee', lastName: `Visitor ${suffix}`, email: `e2e+${suffix}@example.com` }
  const fullName = `${visitor.firstName} ${visitor.lastName}`
  const discounted = seed.studentTicket.price * (1 - seed.studentVoucher.percent / 100) // 640

  let event: CheckInEvent = { id: '', name: '', slug: '' }
  let participantId = ''

  await test.step('admin signs in, sees the seeded HTN Con 2026, and sets up an event happening today', async () => {
    await signInAsBoard(admin)
    event = await createCheckInEvent(admin, suffix)

    await openMainNav(admin, 'Events')
    await expect(admin.getByRole('heading', { name: 'Events', level: 1 })).toBeVisible()
    const events = admin.getByRole('list', { name: 'Events' })
    await expect(events.getByRole('link', { name: new RegExp(seed.event.name) })).toBeVisible()
    await events.getByRole('link', { name: new RegExp(event.name) }).click()
    await expect(admin.getByRole('heading', { name: event.name, level: 1 })).toBeVisible()
    await expect(admin).toHaveURL(new RegExp(`/events/${event.id}/details$`))
  })

  await test.step('visitor registers for a Student ticket with STUDENT20 and DPA consent', async () => {
    const context = await browser.newContext(deviceOptions)
    try {
      const page = await context.newPage()
      await page.goto(`/r/${event.slug}`)
      await expect(page.getByRole('heading', { name: event.name })).toBeVisible()

      await page.getByRole('radio', { name: new RegExp(`^${seed.studentTicket.name}\\b`) }).check()
      await page.getByLabel('First name').fill(visitor.firstName)
      await page.getByLabel('Last name').fill(visitor.lastName)
      const gender = page.getByLabel('Gender')
      if (await gender.isVisible()) await gender.selectOption('Prefer not to say')
      await page.getByLabel('Email address').fill(visitor.email)
      await page.getByLabel('Mobile number').fill('917 123 4567')

      await page.getByLabel('Voucher code').fill(seed.studentVoucher.code)
      await page.getByRole('button', { name: 'Apply' }).click()
      await expect(page.getByText(`${seed.studentVoucher.code} applied`)).toBeVisible()
      await expect(page.getByRole('definition').filter({ hasText: String(discounted) })).toBeVisible()

      const consent = page.getByRole('checkbox', { name: /Data Privacy Act/ })
      await consent.check()
      await expect(consent).toBeChecked()
      await page.getByRole('button', { name: 'Register & get billing details' }).click()

      const success = page.getByRole('region', { name: "You're registered" })
      await expect(success).toBeVisible()
      await expect(success.getByRole('status')).toContainText(visitor.email)
      const idCell = success.getByText(PARTICIPANT_ID_RE)
      await expect(idCell).toHaveCount(1)
      participantId = (await idCell.textContent())?.trim() ?? ''
      expect(participantId).toMatch(PARTICIPANT_ID_RE)
      await expect(success.getByRole('definition').filter({ hasText: String(discounted) })).toBeVisible()
    } finally {
      await context.close()
    }
  })

  await test.step('admin finds the registration by participant ID and marks it paid via GCash', async () => {
    await admin.getByRole('navigation', { name: 'Event sections' }).getByRole('link', { name: 'Registrations' }).click()
    await expect(admin).toHaveURL(new RegExp(`/events/${event.id}/registrations$`))
    await admin.getByRole('searchbox', { name: 'Search registrations' }).fill(participantId)

    const row = admin.getByRole('row').filter({ hasText: participantId })
    await expect(row).toHaveCount(1)
    await expect(row.getByRole('cell', { name: 'Pending payment', exact: true })).toBeVisible()
    await row.getByRole('checkbox', { name: `Select ${fullName}` }).check()
    await admin.getByRole('button', { name: 'Mark as paid & send ticket' }).click()

    const dialog = admin.getByRole('dialog', { name: 'Mark as paid & send ticket' })
    await expect(dialog).toContainText('1 registration')
    const gcash = dialog.getByRole('radio', { name: 'GCash' })
    await gcash.click()
    await expect(gcash).toHaveAttribute('aria-checked', 'true')
    await dialog.getByRole('button', { name: 'Mark as paid & send ticket' }).click()
    await expect(dialog).toBeHidden()
    await expect(admin.getByRole('status').filter({ hasText: '1 marked paid' })).toBeVisible()

    await expect(row.getByRole('cell', { name: 'Paid', exact: true })).toBeVisible()
    await expect(row).toContainText('GCash')
  })

  await test.step('desk: typed participant ID needs confirmation, then Welcome in; a second lookup shows Already checked in', async () => {
    await admin.getByRole('navigation', { name: 'Event sections' }).getByRole('link', { name: 'Check-in' }).click()
    await admin.getByRole('link', { name: /Desk station/ }).click()
    await expect(admin).toHaveURL(new RegExp(`/events/${event.id}/check-in/desk$`))
    const stationHeading = admin.getByRole('heading', { name: `Check-in station · ${event.name}` })
    await expect(stationHeading).toBeVisible()

    const scanField = admin.getByLabel('Scan a ticket, or type a participant ID')
    const result = admin.getByRole('status', { name: 'Scan result' })
    // The confirm button sits outside the live region and is named "Check in {name}" (the form's own button is just "Check in").
    const confirm = admin.getByRole('button', { name: /^Check in \S/ })

    // A typed ID is only a lookup: nobody is checked in until staff compare the person and confirm.
    await scanField.fill(participantId)
    await scanField.press('Enter')
    await expect(result).toContainText('Check the ticket holder')
    await expect(result).toContainText(fullName)
    await expect(result).not.toContainText('Welcome in')
    await confirm.click()
    await expect(result).toContainText('Welcome in')
    await expect(result).toContainText(fullName)
    await expect(result).toContainText(participantId)

    // The desk drops an identical payload seen within 3 s (SAME_PAYLOAD_WINDOW_MS, so a held-up QR isn't sent twice).
    // A reload clears that client-side memory, so the second lookup really reaches the server.
    await admin.reload()
    await expect(stationHeading).toBeVisible()
    await scanField.fill(participantId)
    await scanField.press('Enter')
    await expect(result).toContainText('Check the ticket holder')
    await expect(result).toContainText('Already checked in')
    await expect(result).toContainText(fullName)
    await expect(confirm).toHaveCount(0)
  })

  await test.step('registration list shows the attendee as Checked in', async () => {
    await admin.goto(`/events/${event.id}/registrations`)
    await admin.getByRole('searchbox', { name: 'Search registrations' }).fill(participantId)
    const row = admin.getByRole('row').filter({ hasText: participantId })
    await expect(row.getByRole('cell', { name: 'Checked in', exact: true })).toBeVisible()
  })
})
