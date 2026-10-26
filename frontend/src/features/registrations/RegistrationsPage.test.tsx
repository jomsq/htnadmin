import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { RequirePolicy } from '@/app/auth/RequirePolicy'
import { Policies } from '@/app/auth/policies'
import { EventLayout } from '@/features/events/EventLayout'
import { detail } from '@/test/fixtures/events'
import { list, paymentAccounts, publicEvent, registrationDetail, row, rows } from '@/test/fixtures/registrations'
import { renderRoutes } from '@/test/render'
import { boardMe, goonMe, server, signInAs } from '@/test/server'
import { RegistrationsPage } from './RegistrationsPage'

function renderPage() {
  return renderRoutes(
    [
      {
        path: '/events/:eventId',
        element: <EventLayout />,
        children: [
          {
            path: 'registrations',
            element: <RequirePolicy policy={Policies.RegistrationsManage} />,
            children: [{ index: true, element: <RegistrationsPage /> }],
          },
        ],
      },
    ],
    '/events/e1/registrations',
  )
}

/** Mocks the event, the list (recording each request's query string) and payment accounts. */
function mockApi(items: unknown[] = rows) {
  const requests: URLSearchParams[] = []
  server.use(
    http.get('/api/events/e1', () => HttpResponse.json(detail())),
    http.get('/api/events/e1/registrations', ({ request }) => {
      requests.push(new URL(request.url).searchParams)
      return HttpResponse.json(list(items))
    }),
    http.get('/api/settings/payment-accounts', () => HttpResponse.json(paymentAccounts)),
    http.get('/api/public/events/htn-con-2026', () => HttpResponse.json(publicEvent())),
  )
  return requests
}

describe('Registrations', () => {
  it('lists registrations with payment, amount, status and the Overdue badge', async () => {
    signInAs(boardMe)
    mockApi()
    renderPage()
    const table = await screen.findByRole('table')
    const maria = within(table).getByRole('row', { name: /Maria Santos/ })
    expect(within(maria).getByText('HTN-7K3Q-9XWD')).toHaveClass('font-mono')
    expect(within(maria).getByText('Proof uploaded')).toBeInTheDocument()
    // The registrant's own reference is labelled as theirs, not shown as a confirmed "Ref".
    expect(within(maria).getByText('From registrant: 7741 0923')).toBeInTheDocument()
    expect(within(maria).queryByText(/^Ref /)).not.toBeInTheDocument()
    expect(within(maria).getByText('₱1,500')).toHaveClass('tabular-nums')
    expect(within(maria).getByText('Pending payment')).toBeInTheDocument()
    expect(within(maria).getByText('Oct 25')).toBeInTheDocument()

    const juan = within(table).getByRole('row', { name: /Juan Dela Cruz/ })
    expect(within(juan).getByText('Awaiting proof')).toBeInTheDocument()
    expect(within(juan).getByText('Overdue')).toBeInTheDocument()
    expect(within(juan).getByText('Seat released — payment deadline passed')).toBeInTheDocument()

    const team = within(table).getByRole('row', { name: /Acme Dev Team \(5\)/ })
    expect(within(team).getByText('+4 more')).toBeInTheDocument()
    expect(within(team).getByText('₱6,000')).toBeInTheDocument()

    const carlo = within(table).getByRole('row', { name: /Carlo Mendoza/ })
    expect(within(carlo).getByText('GCash')).toBeInTheDocument()
    expect(within(carlo).getByText('Ref 5512 0034')).toBeInTheDocument()
    expect(within(carlo).getByText('Paid')).toBeInTheDocument()

    expect(screen.getByRole('link', { name: 'Export CSV' })).toHaveAttribute('href', '/api/events/e1/registrations/export.csv')
  })

  it('shows counts on the status chips and filters by status, Overdue and debounced search', async () => {
    signInAs(boardMe)
    const requests = mockApi()
    const user = userEvent.setup()
    renderPage()
    const chips = await screen.findByRole('group', { name: 'Filter by status' })
    await waitFor(() => expect(within(chips).getByRole('button', { name: /^All/ })).toHaveTextContent('312'))
    expect(within(chips).getAllByRole('button').map((b) => b.textContent?.replace(/,/g, '').replace(/\s+/g, ' ').trim())).toEqual([
      'All 312',
      'Pending payment 48',
      'Overdue 6',
      'Paid 240',
      'Checked in 20',
      'Cancelled 4',
    ])

    await user.click(within(chips).getByRole('button', { name: /^Overdue/ }))
    await waitFor(() => expect(requests.at(-1)?.get('overdue')).toBe('true'))
    expect(requests.at(-1)?.get('status')).toBe('PendingPayment')
    expect(within(chips).getByRole('button', { name: /^Overdue/ })).toHaveAttribute('aria-pressed', 'true')

    await user.click(within(chips).getByRole('button', { name: /^Paid/ }))
    await waitFor(() => expect(requests.at(-1)?.get('status')).toBe('Paid'))
    expect(requests.at(-1)?.get('overdue')).toBeNull()

    const before = requests.length
    await user.type(screen.getByRole('searchbox', { name: 'Search registrations' }), 'HTN-7K3Q')
    await waitFor(() => expect(requests.at(-1)?.get('search')).toBe('HTN-7K3Q'))
    // Debounced: one request for the whole word, not one per keystroke.
    expect(requests.length - before).toBe(1)
    expect(screen.getByRole('link', { name: 'Export CSV' })).toHaveAttribute('href', '/api/events/e1/registrations/export.csv?status=Paid&search=HTN-7K3Q')
  })

  it('selects rows, shows the total and marks them paid with the chosen account', async () => {
    signInAs(boardMe)
    mockApi()
    let body: unknown
    server.use(
      http.post('/api/events/e1/registrations/mark-paid', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ updated: 2 })
      }),
    )
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('checkbox', { name: 'Select Maria Santos' }))
    await user.click(screen.getByRole('checkbox', { name: 'Select Juan Dela Cruz' }))
    const bar = screen.getByRole('region', { name: 'Bulk actions' })
    expect(within(bar).getByText('2 selected')).toBeInTheDocument()
    expect(within(bar).getByText('Total ₱3,000')).toBeInTheDocument()

    await user.click(within(bar).getByRole('button', { name: 'Mark as paid & send ticket' }))
    const dialog = await screen.findByRole('dialog', { name: 'Mark as paid & send ticket' })
    expect(within(dialog).getByText(/2 registrations · Total ₱3,000/)).toBeInTheDocument()
    expect(within(dialog).getByRole('radio', { name: 'GCash' })).toHaveAttribute('aria-checked', 'true')
    expect(within(dialog).getByLabelText('Payment account')).toHaveValue('a1')

    await user.click(within(dialog).getByRole('radio', { name: 'Bank transfer' }))
    expect(within(dialog).getByLabelText('Payment account')).toHaveValue('a2')
    await user.type(within(dialog).getByLabelText(/Reference/), 'FT2610-882')
    await user.click(within(dialog).getByRole('button', { name: 'Mark as paid & send ticket' }))

    expect(await screen.findByText('2 marked paid. Ticket emails with QR codes sent.')).toBeInTheDocument()
    expect(body).toMatchObject({ ids: ['r1', 'r2'], method: 'Bank', paymentAccountId: 'a2', reference: 'FT2610-882' })
    expect((body as { receivedAt: string }).receivedAt).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:00\+08:00$/)
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Bulk actions' })).not.toBeInTheDocument())
  })

  it('skips registrations that are not pending when marking paid', async () => {
    signInAs(boardMe)
    mockApi()
    let body: { ids?: string[] } = {}
    server.use(
      http.post('/api/events/e1/registrations/mark-paid', async ({ request }) => {
        body = (await request.json()) as { ids: string[] }
        return HttpResponse.json({ updated: 1 })
      }),
    )
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('checkbox', { name: 'Select Maria Santos' }))
    await user.click(screen.getByRole('checkbox', { name: 'Select Carlo Mendoza' }))
    await user.click(within(screen.getByRole('region', { name: 'Bulk actions' })).getByRole('button', { name: 'Mark as paid & send ticket' }))
    const dialog = await screen.findByRole('dialog', { name: 'Mark as paid & send ticket' })
    expect(within(dialog).getByText(/1 selected registration is not pending payment and will be skipped/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Mark as paid & send ticket' }))
    await screen.findByText('1 marked paid. Ticket emails with QR codes sent.')
    expect(body.ids).toEqual(['r1'])
  })

  it('opens the drawer with the proof image, totals and history', async () => {
    signInAs(boardMe)
    mockApi()
    server.use(http.get('/api/events/e1/registrations/r1', () => HttpResponse.json(registrationDetail())))
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: /Maria Santos/ }))
    const drawer = await screen.findByRole('dialog', { name: 'Registration details' })
    const img = await within(drawer).findByRole('img', { name: 'Proof of payment uploaded by Maria Santos' })
    expect(img).toHaveAttribute('src', '/api/events/e1/registrations/r1/proof')
    expect(within(drawer).getByText('7741 0923')).toBeInTheDocument()
    expect(within(drawer).getByText('Reference (from registrant)')).toBeInTheDocument()
    expect(within(drawer).getByText('Not paid yet.')).toBeInTheDocument()
    expect(within(drawer).getByText('Registered')).toBeInTheDocument()
    expect(within(drawer).getByRole('button', { name: 'Resend billing email' })).toBeInTheDocument()
    expect(within(drawer).queryByRole('button', { name: 'Revert payment' })).not.toBeInTheDocument()
  })

  it('requires a reason to cancel a registration', async () => {
    signInAs(boardMe)
    mockApi()
    let body: unknown = null
    server.use(
      http.get('/api/events/e1/registrations/r1', () => HttpResponse.json(registrationDetail())),
      http.post('/api/events/e1/registrations/r1/cancel', async ({ request }) => {
        body = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: /Maria Santos/ }))
    const drawer = await screen.findByRole('dialog', { name: 'Registration details' })
    await user.click(within(drawer).getByRole('button', { name: 'Cancel registration' }))
    const confirm = await screen.findByRole('alertdialog', { name: "Cancel Maria Santos's registration?" })
    await user.click(within(confirm).getByRole('button', { name: 'Cancel registration' }))
    expect(await within(confirm).findByText('Enter a reason.')).toBeInTheDocument()
    expect(within(confirm).getByLabelText('Reason')).toHaveAttribute('aria-invalid', 'true')
    expect(body).toBeNull()

    await user.type(within(confirm).getByLabelText('Reason'), 'Asked to cancel by email')
    await user.click(within(confirm).getByRole('button', { name: 'Cancel registration' }))
    expect(await screen.findByText("Maria Santos's registration was cancelled.")).toBeInTheDocument()
    expect(body).toEqual({ reason: 'Asked to cancel by email' })
  })

  it('requires a reason to revert a payment', async () => {
    signInAs(boardMe)
    mockApi([row({ status: 'Paid', method: 'GCash', reference: '5512' })])
    let body: unknown = null
    server.use(
      http.get('/api/events/e1/registrations/r1', () =>
        HttpResponse.json(registrationDetail({ status: 'Paid', method: 'GCash', payment: { method: 'GCash', reference: '5512', amount: 1500, receivedAt: '2026-10-25T06:00:00Z', markedByName: 'Alex Reyes' } })),
      ),
      http.post('/api/events/e1/registrations/r1/revert-payment', async ({ request }) => {
        body = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: /Maria Santos/ }))
    const drawer = await screen.findByRole('dialog', { name: 'Registration details' })
    expect(await within(drawer).findByText('Alex Reyes')).toBeInTheDocument()
    await user.click(within(drawer).getByRole('button', { name: 'Revert payment' }))
    const confirm = await screen.findByRole('alertdialog', { name: 'Revert this payment?' })
    await user.click(within(confirm).getByRole('button', { name: 'Revert payment' }))
    expect(await within(confirm).findByText('Enter a reason.')).toBeInTheDocument()
    expect(body).toBeNull()
    await user.type(within(confirm).getByLabelText('Reason'), 'Wrong reference')
    await user.click(within(confirm).getByRole('button', { name: 'Revert payment' }))
    expect(await screen.findByText('Payment reverted.')).toBeInTheDocument()
    expect(body).toEqual({ reason: 'Wrong reference' })
  })

  it('validates the manual registration form and sends a hidden comp ticket marked paid', async () => {
    signInAs(boardMe)
    mockApi()
    let body: Record<string, unknown> | null = null
    server.use(
      http.post('/api/events/e1/registrations', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>
        return HttpResponse.json({ participantIds: ['HTN-B3NZ-8HVA'], amountDue: 0, deadline: null, proofMode: 'Upload', email: 'pat@sponsorco.com' }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: '+ Manual registration' }))
    const dialog = await screen.findByRole('dialog', { name: 'Manual registration' })
    await user.click(within(dialog).getByRole('button', { name: 'Register & send billing' }))
    expect(await within(dialog).findByText('Enter the first name.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter the last name.')).toBeInTheDocument()
    expect(within(dialog).getByText('Select a gender.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter an email address.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter a mobile number like 917 123 4567.')).toBeInTheDocument()
    expect(within(dialog).getByText('Confirm the attendee agreed to the data privacy terms.')).toBeInTheDocument()
    expect(body).toBeNull()

    const ticket = within(dialog).getByLabelText('Ticket')
    expect(within(ticket).getByRole('option', { name: /Sponsor comp · ₱0 · hidden/ })).toBeInTheDocument()
    await user.selectOptions(ticket, 't4')
    await user.type(within(dialog).getByLabelText('First name'), 'Pat')
    await user.type(within(dialog).getByLabelText('Last name'), 'Villar')
    await user.selectOptions(within(dialog).getByLabelText('Gender'), 'Prefer not to say')
    await user.type(within(dialog).getByLabelText('Email address'), 'pat@sponsorco.com')
    await user.type(within(dialog).getByLabelText('Mobile number'), '12345')
    await user.click(within(dialog).getByRole('checkbox', { name: /The attendee agreed/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Register & send billing' }))
    expect(await within(dialog).findByText('Enter a mobile number like 917 123 4567.')).toBeInTheDocument()

    await user.clear(within(dialog).getByLabelText('Mobile number'))
    await user.type(within(dialog).getByLabelText('Mobile number'), '0917 123 4567')
    await user.click(within(dialog).getByRole('checkbox', { name: 'Mark as paid now and send the ticket' }))
    expect(within(dialog).getByRole('radio', { name: 'Comp' })).toHaveAttribute('aria-checked', 'true')
    await user.click(within(dialog).getByRole('button', { name: 'Register & send ticket' }))

    expect(await screen.findByText('Pat Villar is registered.')).toBeInTheDocument()
    expect(body).toMatchObject({
      ticketTypeId: 't4',
      consent: true,
      attendees: [{ firstName: 'Pat', lastName: 'Villar', gender: 'Prefer not to say', email: 'pat@sponsorco.com', mobile: '+639171234567' }],
      markPaid: { method: 'Comp' },
    })
    expect(body).not.toHaveProperty('website')
    expect(body).not.toHaveProperty('idempotencyKey')
  })

  it('hides the page, bulk actions and manual registration from goons', async () => {
    signInAs(goonMe)
    mockApi()
    renderPage()
    expect(await screen.findByText('You do not have access to this page')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Manual registration' })).not.toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    const tabs = screen.getByRole('navigation', { name: 'Event sections' })
    expect(within(tabs).getAllByRole('link').map((a) => a.textContent)).toEqual(['Details', 'Check-in'])
  })
})
