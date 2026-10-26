import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { boardMe, goonMe, problem, server, signInAs } from '@/test/server'
import { detail, draftDetail } from '@/test/fixtures/events'
import { renderRoutes } from '@/test/render'
import { EventDetailsPage } from './EventDetailsPage'
import { EventLayout } from './EventLayout'

function renderDetails(id = 'e1') {
  return renderRoutes(
    [{ path: '/events/:eventId', element: <EventLayout />, children: [{ path: 'details', element: <EventDetailsPage /> }] }],
    `/events/${id}/details`,
  )
}

function mockEvent(data: Record<string, unknown> = detail()) {
  server.use(
    http.get(`/api/events/${String(data.id)}`, () => HttpResponse.json(data)),
    http.get('/api/settings/payment-accounts', () =>
      HttpResponse.json([
        {
          id: 'a1',
          kind: 'GCash',
          label: 'GCash — HTN',
          bankName: null,
          accountName: 'HackTheNorth.PH Inc.',
          accountNumber: '0917 123 4567',
          accountNumberMasked: '•••• 4567',
          isDefault: true,
          isActive: true,
        },
      ]),
    ),
  )
}

describe('Event layout', () => {
  it('shows the header, preview link and only the built tabs', async () => {
    signInAs(boardMe)
    mockEvent()
    renderDetails()
    expect(await screen.findByRole('heading', { level: 1, name: 'HTN Con 2026' })).toBeInTheDocument()
    expect(screen.getAllByText('On sale').length).toBeGreaterThan(0)
    const preview = screen.getByRole('link', { name: /Preview registration page/ })
    expect(preview).toHaveAttribute('href', '/r/htn-con-2026')
    expect(preview).toHaveAttribute('target', '_blank')
    expect(preview).toHaveAttribute('rel', 'noopener noreferrer')
    const tabs = screen.getByRole('navigation', { name: 'Event sections' })
    expect(within(tabs).getAllByRole('link').map((a) => a.textContent)).toEqual(['Details', 'Registrations', 'Emails', 'Check-in'])
    expect(within(tabs).getByRole('link', { name: 'Details' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('link', { name: 'Events' })).toHaveAttribute('href', '/events')
    expect(await screen.findByRole('button', { name: 'Close registration' })).toBeInTheDocument()
  })

  it('shows the no_tickets error when publishing a draft without tickets', async () => {
    signInAs(boardMe)
    mockEvent(draftDetail())
    server.use(http.post('/api/events/e3/publish', () => problem(409, 'Conflict', 'no_tickets')))
    const user = userEvent.setup()
    renderDetails('e3')
    await user.click(await screen.findByRole('button', { name: 'Publish' }))
    expect(await screen.findByText('Could not publish the event.')).toBeInTheDocument()
    expect(screen.getByText('Add at least one active ticket type before publishing.')).toBeInTheDocument()
    const steps = screen.getByRole('navigation', { name: 'Setup steps' })
    expect(within(steps).getByRole('link', { name: /Tickets\s*\(to do\)/ })).toBeInTheDocument()
    expect(within(steps).getByRole('link', { name: /Event info\s*\(complete\)/ })).toBeInTheDocument()
  })

  it('shows 404 as "Event not found"', async () => {
    signInAs(goonMe)
    server.use(http.get('/api/events/e9', () => problem(404, 'Not Found')))
    renderDetails('e9')
    expect(await screen.findByText('Event not found')).toBeInTheDocument()
  })
})

describe('Details › Tickets', () => {
  it('lists ticket types with pesos, sold / qty, group size and status', async () => {
    signInAs(boardMe)
    mockEvent()
    renderDetails()
    const table = await screen.findByRole('table', { name: 'Ticket types' })
    const regular = within(table).getByText('Regular').closest('tr') as HTMLElement
    expect(within(regular).getByText('₱1,500')).toBeInTheDocument()
    expect(within(regular).getByText('121 / 200')).toBeInTheDocument()
    expect(within(regular).getByText('Oct 16 – Nov 20')).toBeInTheDocument()
    const team = within(table).getByText('Team of 5').closest('tr') as HTMLElement
    expect(within(team).getByText('2 / 10 groups')).toBeInTheDocument()
    expect(within(team).getByText('5 people')).toBeInTheDocument()
    const comp = within(table).getByText('Sponsor comp').closest('tr') as HTMLElement
    expect(within(comp).getByText('Free')).toBeInTheDocument()
    expect(within(comp).getByText('Hidden')).toBeInTheDocument()
    expect(within(comp).getByText('Assigned by admin')).toBeInTheDocument()
  })

  it('validates the ticket dialog and adds a ticket type', async () => {
    signInAs(boardMe)
    mockEvent()
    let body: unknown
    server.use(
      http.post('/api/events/e1/tickets', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderDetails()
    await user.click(await screen.findByRole('button', { name: '+ Ticket type' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add ticket type' })
    await user.clear(within(dialog).getByLabelText('Group size'))
    await user.click(within(dialog).getByRole('button', { name: 'Add ticket type' }))
    expect(await within(dialog).findByText('Enter a ticket name.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter a price like 1500 or 1500.50. Use 0 for free.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter a whole number from 1 to 100,000.')).toBeInTheDocument()
    expect(within(dialog).getByText('Use 1 to 50 people.')).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText('Name'), 'Early bird')
    await user.type(within(dialog).getByLabelText('Price (₱)'), '1,200.50')
    await user.type(within(dialog).getByLabelText('Quantity'), '150')
    await user.type(within(dialog).getByLabelText('Group size'), '1')
    await user.type(within(dialog).getByLabelText(/Sales start/), '2026-09-01T00:00')
    await user.type(within(dialog).getByLabelText(/Sales end/), '2026-08-01T00:00')
    await user.click(within(dialog).getByRole('button', { name: 'Add ticket type' }))
    expect(await within(dialog).findByText('Sales must end after they start.')).toBeInTheDocument()

    await user.clear(within(dialog).getByLabelText(/Sales end/))
    await user.type(within(dialog).getByLabelText(/Sales end/), '2026-10-15T23:59')
    await user.click(within(dialog).getByRole('radio', { name: 'Hidden' }))
    await user.click(within(dialog).getByRole('button', { name: 'Add ticket type' }))
    expect(await screen.findByText('Early bird added.')).toBeInTheDocument()
    expect(body).toEqual({
      name: 'Early bird',
      price: 1200.5,
      quantity: 150,
      groupSize: 1,
      salesStartsAt: '2026-09-01T00:00:00+08:00',
      salesEndsAt: '2026-10-15T23:59:00+08:00',
      visibility: 'Hidden',
    })
    expect(screen.queryByRole('dialog', { name: 'Add ticket type' })).not.toBeInTheDocument()
  })

  it('locks the price once sold and keeps quantity at or above sold', async () => {
    signInAs(boardMe)
    mockEvent()
    const user = userEvent.setup()
    renderDetails()
    await user.click(await screen.findByRole('button', { name: 'Edit Regular' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit Regular' })
    expect(within(dialog).getByLabelText('Price (₱)')).toHaveAttribute('readonly')
    expect(within(dialog).getByLabelText('Price (₱)')).toHaveValue('1500')
    const qty = within(dialog).getByLabelText('Quantity')
    await user.clear(qty)
    await user.type(qty, '100')
    await user.click(within(dialog).getByRole('button', { name: 'Save ticket type' }))
    expect(await within(dialog).findByText("Can't be below the 121 already sold.")).toBeInTheDocument()
  })

  it('deactivates a ticket type after confirming', async () => {
    signInAs(boardMe)
    mockEvent()
    let called = false
    server.use(
      http.post('/api/events/e1/tickets/t2/deactivate', () => {
        called = true
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderDetails()
    await user.click(await screen.findByRole('button', { name: 'Deactivate Student' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Deactivate Student?' })
    await user.click(within(dialog).getByRole('button', { name: 'Deactivate' }))
    expect(await screen.findByText('Student was deactivated.')).toBeInTheDocument()
    expect(called).toBe(true)
  })
})

describe('Details › Vouchers', () => {
  it('lists vouchers with discount, scope and usage', async () => {
    signInAs(boardMe)
    mockEvent()
    renderDetails()
    const table = await screen.findByRole('table', { name: 'Vouchers' })
    const student = within(table).getByText('STUDENT20').closest('tr') as HTMLElement
    expect(within(student).getByText('20% off')).toBeInTheDocument()
    expect(within(student).getByText('Student')).toBeInTheDocument()
    expect(within(student).getByText('12 / 50')).toBeInTheDocument()
    const partner = within(table).getByText('PARTNER300').closest('tr') as HTMLElement
    expect(within(partner).getByText('₱300 off')).toBeInTheDocument()
    expect(within(partner).getByText('All tickets')).toBeInTheDocument()
    expect(within(partner).getByText('4 / no limit')).toBeInTheDocument()
  })

  it('keeps a percent voucher within 1–100 and saves the code upper-case', async () => {
    signInAs(boardMe)
    mockEvent()
    let body: unknown
    server.use(
      http.post('/api/events/e1/vouchers', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderDetails()
    await user.click(await screen.findByRole('button', { name: '+ Voucher' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add voucher' })
    await user.type(within(dialog).getByLabelText('Code'), 'go')
    await user.type(within(dialog).getByLabelText('Percent off'), '150')
    await user.click(within(dialog).getByRole('button', { name: 'Add voucher' }))
    expect(await within(dialog).findByText('Use 3 to 30 letters, numbers or dashes.')).toBeInTheDocument()
    expect(within(dialog).getByText('Use 1 to 100 percent.')).toBeInTheDocument()

    await user.clear(within(dialog).getByLabelText('Percent off'))
    await user.type(within(dialog).getByLabelText('Percent off'), '0')
    await user.click(within(dialog).getByRole('button', { name: 'Add voucher' }))
    expect(await within(dialog).findByText('Use 1 to 100 percent.')).toBeInTheDocument()

    await user.clear(within(dialog).getByLabelText('Code'))
    await user.type(within(dialog).getByLabelText('Code'), 'goonfam')
    await user.clear(within(dialog).getByLabelText('Percent off'))
    await user.type(within(dialog).getByLabelText('Percent off'), '100')
    await user.click(within(dialog).getByRole('checkbox', { name: 'Regular' }))
    await user.type(within(dialog).getByLabelText(/Usage limit/), '20')
    await user.click(within(dialog).getByRole('button', { name: 'Add voucher' }))
    expect(await screen.findByText('GOONFAM added.')).toBeInTheDocument()
    expect(body).toEqual({ code: 'GOONFAM', kind: 'Percent', value: 100, ticketTypeIds: ['t1'], usageLimit: 20, expiresAt: null })
  })

  it('switches to a fixed peso amount and shows duplicate_code on the field', async () => {
    signInAs(boardMe)
    mockEvent()
    server.use(http.post('/api/events/e1/vouchers', () => problem(409, 'Conflict', 'duplicate_code')))
    const user = userEvent.setup()
    renderDetails()
    await user.click(await screen.findByRole('button', { name: '+ Voucher' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add voucher' })
    await user.click(within(dialog).getByRole('radio', { name: '₱ off' }))
    await user.type(within(dialog).getByLabelText('Code'), 'PARTNER300')
    await user.type(within(dialog).getByLabelText('Amount off (₱)'), '0')
    await user.click(within(dialog).getByRole('button', { name: 'Add voucher' }))
    expect(await within(dialog).findByText('Enter an amount more than ₱0, like 300.')).toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText('Amount off (₱)'))
    await user.type(within(dialog).getByLabelText('Amount off (₱)'), '300')
    await user.click(within(dialog).getByRole('button', { name: 'Add voucher' }))
    expect(await within(dialog).findByText('This voucher code is already used for this event.')).toBeInTheDocument()
  })
})

describe('Details › Registration & payment', () => {
  it('saves toggles and proof mode with the rest of the event unchanged', async () => {
    signInAs(boardMe)
    mockEvent()
    let body: Record<string, unknown> = {}
    server.use(
      http.put('/api/events/e1', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(detail({ ...body }))
      }),
    )
    const user = userEvent.setup()
    renderDetails()
    await user.click(await screen.findByRole('switch', { name: 'Group registration' }))
    await user.click(screen.getByRole('radio', { name: 'Reply to email' }))
    const fee = screen.getByLabelText('Membership fee (₱)')
    await user.clear(fee)
    await user.click(screen.getByRole('button', { name: 'Save registration settings' }))
    expect(await screen.findByText('Enter the fee in pesos, like 300.')).toBeInTheDocument()
    await user.type(fee, '350')
    await user.click(screen.getByRole('button', { name: 'Save registration settings' }))
    expect(await screen.findByText('Registration settings saved.')).toBeInTheDocument()
    expect(body).toMatchObject({
      name: 'HTN Con 2026',
      slug: 'htn-con-2026',
      capacity: 400,
      manualRegistration: true,
      groupRegistration: false,
      publicPage: true,
      proofMode: 'ReplyEmail',
      isMainEvent: true,
      membershipFee: 350,
      membershipYear: 2026,
    })
    expect(screen.getByRole('list', { name: 'Payment accounts' })).toHaveTextContent('GCash — HTN')
  })
})

describe('Details for non-managers', () => {
  it('is read-only for a goon: no publish, edit, vouchers or save', async () => {
    signInAs(goonMe)
    mockEvent(detail({ vouchers: [] }))
    renderDetails()
    expect(await screen.findByText(/View only/)).toBeInTheDocument()
    await waitFor(() => expect(screen.getByLabelText('Event name')).toBeDisabled())
    expect(screen.getByRole('table', { name: 'Ticket types' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Close registration' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Publish' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Ticket type' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Edit / })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Deactivate / })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Vouchers' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Save/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /cover image/ })).not.toBeInTheDocument()
    expect(screen.getByRole('switch', { name: 'Public registration page' })).toBeDisabled()
    expect(screen.getByRole('link', { name: /Preview registration page/ })).toBeInTheDocument()
  })
})
