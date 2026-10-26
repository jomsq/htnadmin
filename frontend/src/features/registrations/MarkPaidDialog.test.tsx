import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { paymentAccounts } from '@/test/fixtures/registrations'
import { renderWithProviders } from '@/test/render'
import { boardMe, problem, server, signInAs } from '@/test/server'
import { MarkPaidDialog, type MarkPaidTarget } from './components/MarkPaidDialog'

type Body = Record<string, unknown>

function setup(targets: MarkPaidTarget[], reply?: (body: Body, call: number) => Response) {
  signInAs(boardMe)
  const bodies: Body[] = []
  server.use(
    http.get('/api/settings/payment-accounts', () => HttpResponse.json(paymentAccounts)),
    http.post('/api/events/e1/registrations/mark-paid', async ({ request }) => {
      const body = (await request.json()) as Body
      bodies.push(body)
      return reply ? reply(body, bodies.length) : HttpResponse.json({ updated: targets.length })
    }),
  )
  const onDone = vi.fn()
  renderWithProviders(<MarkPaidDialog eventId="e1" targets={targets} onClose={vi.fn()} onDone={onDone} />)
  return { bodies, onDone, user: userEvent.setup() }
}

/** Waits until the payment accounts have loaded and the form is shown. */
async function openForm() {
  // The loading state is a separate dialog, so query again once the form is there.
  await screen.findByRole('radio', { name: 'Comp' })
  return screen.getByRole('dialog', { name: 'Mark as paid & send ticket' })
}

const paidTicket: MarkPaidTarget = { id: 'r1', status: 'PendingPayment', total: 1500 }
const freeTicket: MarkPaidTarget = { id: 'r2', status: 'PendingPayment', total: 0 }

describe('Mark as paid', () => {
  it('requires a reason to comp a registration that costs something, and sends it', async () => {
    const { bodies, onDone, user } = setup([paidTicket, freeTicket])
    const dialog = await openForm()
    await user.click(within(dialog).getByRole('radio', { name: 'Comp' }))
    await user.click(within(dialog).getByRole('button', { name: 'Mark as paid & send ticket' }))
    expect(await within(dialog).findByText('Enter why this registration is comped. It is kept in the history.')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Reason for comping')).toHaveAttribute('aria-invalid', 'true')
    expect(bodies).toHaveLength(0)

    await user.type(within(dialog).getByLabelText('Reason for comping'), 'Sponsor guest')
    await user.click(within(dialog).getByRole('button', { name: 'Mark as paid & send ticket' }))
    await waitFor(() => expect(onDone).toHaveBeenCalled())
    expect(bodies[0]).toMatchObject({ ids: ['r1', 'r2'], method: 'Comp', reason: 'Sponsor guest' })
    expect(bodies[0].overrideCapacityReason).toBeUndefined()
  })

  it('lets a free registration be comped without a reason', async () => {
    const { bodies, onDone, user } = setup([freeTicket])
    const dialog = await openForm()
    await user.click(within(dialog).getByRole('radio', { name: 'Comp' }))
    expect(within(dialog).getByLabelText(/Reason for comping/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Mark as paid & send ticket' }))
    await waitFor(() => expect(onDone).toHaveBeenCalled())
    expect(bodies[0].reason).toBeUndefined()
  })

  it('asks for an override reason when the event is full now, then resends with it', async () => {
    const { bodies, onDone, user } = setup([paidTicket], (_body, call) =>
      call === 1 ? problem(409, 'Conflict', 'capacity_exceeded') : HttpResponse.json({ updated: 1 }),
    )
    const dialog = await openForm()
    expect(within(dialog).queryByLabelText('Override reason')).not.toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Mark as paid & send ticket' }))
    expect(await within(dialog).findByText('This event or ticket is full now. Add a reason to mark it paid anyway.')).toBeInTheDocument()
    const override = within(dialog).getByLabelText('Override reason')
    expect(onDone).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: 'Mark as paid & send ticket' }))
    expect(await within(dialog).findByText('Enter why it should be marked paid even though it is full.')).toBeInTheDocument()
    expect(bodies).toHaveLength(1)

    await user.type(override, 'Paid on time, proof was delayed')
    await user.click(within(dialog).getByRole('button', { name: 'Mark as paid & send ticket' }))
    await waitFor(() => expect(onDone).toHaveBeenCalled())
    expect(bodies[1]).toMatchObject({ ids: ['r1'], method: 'GCash', overrideCapacityReason: 'Paid on time, proof was delayed' })
  })
})
