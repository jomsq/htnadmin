import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { RequirePolicy } from '@/app/auth/RequirePolicy'
import { Policies } from '@/app/auth/policies'
import { EventLayout } from '@/features/events/EventLayout'
import { detail } from '@/test/fixtures/events'
import { paymentAccounts } from '@/test/fixtures/registrations'
import { renderRoutes } from '@/test/render'
import { boardMe, goonMe, server, signInAs } from '@/test/server'
import { EmailSettingsPage } from './EmailSettingsPage'

function renderPage() {
  return renderRoutes(
    [
      {
        path: '/events/:eventId',
        element: <EventLayout />,
        children: [
          {
            path: 'emails',
            element: <RequirePolicy policy={Policies.EventsManage} />,
            children: [{ index: true, element: <EmailSettingsPage /> }],
          },
        ],
      },
    ],
    '/events/e1/emails',
  )
}

function mockApi(settings: Record<string, unknown> = { accentColor: null, paymentAccountIds: [], paymentDeadlineHours: null }) {
  server.use(
    http.get('/api/events/e1', () => HttpResponse.json(detail())),
    http.get('/api/events/e1/email-settings', () => HttpResponse.json(settings)),
    http.get('/api/settings/payment-accounts', () => HttpResponse.json(paymentAccounts)),
  )
}

describe('Email settings', () => {
  it('previews the billing and ticket emails and saves color, accounts and deadline', async () => {
    signInAs(boardMe)
    mockApi()
    let body: unknown
    server.use(
      http.put('/api/events/e1/email-settings', async ({ request }) => {
        body = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderPage()
    const preview = await screen.findByRole('article', { name: 'Billing email preview' })
    expect(screen.getByText('Subject: Complete your payment for HTN Con 2026')).toBeInTheDocument()
    expect(within(preview).getByText('Complete your payment, Maria')).toBeInTheDocument()
    expect(within(preview).getByText('Upload proof of payment')).toBeInTheDocument()
    // Defaults: org brand color, the default account and the org deadline.
    expect(screen.getByLabelText('Hex')).toHaveValue('#B71C1C')
    expect(screen.getByRole('checkbox', { name: /GCash — HTN/ })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: /BPI — HTN/ })).not.toBeChecked()
    expect(screen.getByLabelText('Payment deadline')).toHaveValue('48')
    expect(within(preview).getByText(/held for 48 hours/)).toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: 'Teal #0F766E' }))
    await user.click(screen.getByRole('checkbox', { name: /BPI — HTN/ }))
    expect(within(preview).getByText('BANK TRANSFER · BPI')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Payment deadline'), 'custom')
    await user.type(screen.getByLabelText('Hours'), '36')
    expect(within(preview).getByText(/held for 36 hours/)).toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: '2 · Ticket (after paid)' }))
    const ticket = screen.getByRole('article', { name: 'Ticket email preview' })
    expect(within(ticket).getByText("You're in, Maria")).toBeInTheDocument()
    expect(screen.getByText('Subject: Your HTN Con 2026 ticket')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Save email settings' }))
    expect(await screen.findByText('Email settings saved.')).toBeInTheDocument()
    expect(body).toEqual({ accentColor: '#0F766E', paymentAccountIds: ['a1', 'a2'], paymentDeadlineHours: 36 })
  })

  it('warns about low contrast and blocks saving', async () => {
    signInAs(boardMe)
    mockApi({ accentColor: '#1D4ED8', paymentAccountIds: ['a2'], paymentDeadlineHours: 72 })
    const user = userEvent.setup()
    renderPage()
    const hex = await screen.findByLabelText('Hex')
    expect(hex).toHaveValue('#1D4ED8')
    expect(screen.getByLabelText('Payment deadline')).toHaveValue('72')
    expect(screen.getByRole('checkbox', { name: /BPI — HTN/ })).toBeChecked()
    await user.clear(hex)
    await user.type(hex, '#F87171')
    expect(await screen.findByText(/Low contrast: 2\.77:1 with white text/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save email settings' })).toBeDisabled()
  })

  it('requires at least one payment account', async () => {
    signInAs(boardMe)
    mockApi({ accentColor: null, paymentAccountIds: ['a1'], paymentDeadlineHours: 24 })
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('checkbox', { name: /GCash — HTN/ }))
    await user.click(screen.getByRole('button', { name: 'Save email settings' }))
    expect(await screen.findByText('Pick at least one account for the billing email.')).toBeInTheDocument()
  })

  it('sends a test email of the selected template', async () => {
    signInAs(boardMe)
    mockApi()
    let body: unknown
    server.use(
      http.post('/api/events/e1/email-settings/test', async ({ request }) => {
        body = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('radio', { name: '2 · Ticket (after paid)' }))
    await user.click(screen.getByRole('button', { name: 'Send test email' }))
    expect(await screen.findByText('Test ticket email sent to alex@hackthenorth.ph.')).toBeInTheDocument()
    expect(body).toEqual({ template: 'ticket' })
  })

  it('is board only: goons see no settings and no test button', async () => {
    signInAs(goonMe)
    mockApi()
    renderPage()
    expect(await screen.findByText('You do not have access to this page')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Send test email' })).not.toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Save email settings' })).not.toBeInTheDocument()
  })
})
