import { screen, waitFor, within } from '@testing-library/react'
import userEvent, { type UserEvent } from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { publicEvent } from '@/test/fixtures/registrations'
import { renderRoutes } from '@/test/render'
import { CSRF, problem, server } from '@/test/server'
import { PublicRegistrationPage } from './PublicRegistrationPage'

type Body = { idempotencyKey: string; ticketTypeId: string; voucherCode?: string; attendees: Array<Record<string, unknown>>; consent: boolean; website: string }

function renderPage() {
  return renderRoutes([{ path: '/r/:slug', element: <PublicRegistrationPage /> }], '/r/htn-con-2026')
}

const result = (over: Record<string, unknown> = {}) => ({
  participantIds: ['HTN-7K3Q-9XWD'],
  amountDue: 1500,
  deadline: '2026-10-27T09:00:00Z',
  proofMode: 'Upload',
  email: 'maria.santos@gmail.com',
  ...over,
})

function mockEvent(over: Record<string, unknown> = {}) {
  server.use(http.get('/api/public/events/htn-con-2026', () => HttpResponse.json(publicEvent(over))))
}

/** Records registration posts (body + CSRF header) and answers with `reply`. */
function mockSubmit(reply: () => Response = () => HttpResponse.json(result(), { status: 201 })) {
  const posts: Array<{ body: Body; csrf: string | null }> = []
  server.use(
    http.post('/api/public/events/htn-con-2026/registrations', async ({ request }) => {
      posts.push({ body: (await request.json()) as Body, csrf: request.headers.get('X-CSRF-TOKEN') })
      return reply()
    }),
  )
  return posts
}

async function fillBuyer(user: UserEvent, mobile = '917 123 4567') {
  await user.type(screen.getByLabelText('First name'), 'Maria')
  await user.type(screen.getByLabelText('Last name'), 'Santos')
  await user.selectOptions(screen.getByLabelText('Gender'), 'Female')
  await user.type(screen.getByLabelText('Email address'), 'maria.santos@gmail.com')
  await user.type(screen.getByLabelText('Mobile number'), mobile)
}

describe('Public registration', () => {
  it('shows the event, ticket cards with remaining seats and a disabled sold-out ticket', async () => {
    mockEvent()
    renderPage()
    expect(await screen.findByRole('heading', { level: 1, name: 'HTN Con 2026' })).toBeInTheDocument()
    expect(screen.getByText(/SMX Convention Center, Pasay/)).toBeInTheDocument()
    const tickets = screen.getByRole('group', { name: 'Ticket' })
    const regular = within(tickets).getByRole('radio', { name: /Regular/ })
    expect(regular).toBeChecked()
    expect(within(tickets).getByText('79 left')).toBeInTheDocument()
    const student = within(tickets).getByRole('radio', { name: /Student/ })
    expect(student).toBeDisabled()
    expect(within(tickets).getByText('Sold out')).toBeInTheDocument()
    // Shirt sizes are 44px radio buttons.
    expect(screen.getByRole('radio', { name: 'XL' })).toHaveClass('h-11')
    expect(screen.getByText(mockText(publicEvent().consentText))).toBeInTheDocument()

    const honeypot = document.querySelector<HTMLInputElement>('input[name="website"]')
    expect(honeypot).not.toBeNull()
    expect(honeypot).toHaveAttribute('tabindex', '-1')
    expect(honeypot).toHaveAttribute('autocomplete', 'off')
    expect(honeypot).toHaveAttribute('aria-hidden', 'true')
  })

  it('validates the form: required fields, +63 mobile and the privacy consent', async () => {
    mockEvent()
    const posts = mockSubmit()
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Register & get billing details' }))
    expect(await screen.findByText('Enter the first name.')).toBeInTheDocument()
    expect(screen.getByText('Enter the last name.')).toBeInTheDocument()
    expect(screen.getByText('Select a gender.')).toBeInTheDocument()
    expect(screen.getByText('Enter an email address.')).toBeInTheDocument()
    expect(screen.getByText('Enter a mobile number like 917 123 4567.')).toBeInTheDocument()
    expect(screen.getByText('Please agree to continue.')).toBeInTheDocument()
    expect(screen.getByLabelText('First name')).toHaveAttribute('aria-invalid', 'true')

    await fillBuyer(user, '817 123 4567')
    await user.click(screen.getByRole('button', { name: 'Register & get billing details' }))
    // Philippine mobile numbers start with 9 after +63.
    expect(await screen.findByText('Enter a mobile number like 917 123 4567.')).toBeInTheDocument()
    expect(posts).toHaveLength(0)

    await user.clear(screen.getByLabelText('Mobile number'))
    await user.type(screen.getByLabelText('Mobile number'), '+63 917 123 4567')
    await user.click(screen.getByRole('button', { name: 'Register & get billing details' }))
    expect(await screen.findByText('Please agree to continue.')).toBeInTheDocument()
    expect(posts).toHaveLength(0)

    await user.click(screen.getByRole('checkbox', { name: /Data Privacy Act/ }))
    await user.click(screen.getByRole('radio', { name: 'M' }))
    await user.click(screen.getByRole('button', { name: 'Register & get billing details' }))
    await screen.findByRole('heading', { name: "You're registered" })
    expect(posts).toHaveLength(1)
    const { body, csrf } = posts[0]
    expect(csrf).toBe(CSRF)
    expect(body.idempotencyKey).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(body).toMatchObject({ ticketTypeId: 't1', consent: true, website: '' })
    expect(body.voucherCode).toBeUndefined()
    expect(body.attendees).toEqual([
      { firstName: 'Maria', lastName: 'Santos', gender: 'Female', email: 'maria.santos@gmail.com', mobile: '+639171234567', shirtSize: 'M' },
    ])
  })

  it('shows the upload guidance on success and starts a fresh form for someone else', async () => {
    mockEvent()
    const posts = mockSubmit(() => HttpResponse.json(result(), { status: 201 }))
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('group', { name: 'Ticket' })
    await fillBuyer(user)
    await user.click(screen.getByRole('checkbox', { name: /Data Privacy Act/ }))
    await user.click(screen.getByRole('button', { name: 'Register & get billing details' }))

    const heading = await screen.findByRole('heading', { name: "You're registered" })
    await waitFor(() => expect(heading).toHaveFocus())
    expect(screen.getByText(/upload your proof of payment using the link in that email/)).toBeInTheDocument()
    expect(screen.queryByText(/reply to the billing email/)).not.toBeInTheDocument()
    expect(screen.getByText('HTN-7K3Q-9XWD').closest('dd')).toHaveClass('font-mono')
    expect(screen.getByText('₱1,500')).toBeInTheDocument()
    expect(screen.getByText('maria.santos@gmail.com')).toBeInTheDocument()
    // The upload link comes in the billing email only.
    expect(screen.queryByRole('link', { name: 'Upload proof of payment' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Register someone else' }))
    expect(await screen.findByLabelText('First name')).toHaveValue('')
    await fillBuyer(user)
    await user.click(screen.getByRole('checkbox', { name: /Data Privacy Act/ }))
    await user.click(screen.getByRole('button', { name: 'Register & get billing details' }))
    await screen.findByRole('heading', { name: "You're registered" })
    expect(posts).toHaveLength(2)
    expect(posts[1].body.idempotencyKey).not.toBe(posts[0].body.idempotencyKey)
  })

  it('tells reply-by-email registrants to answer the billing email', async () => {
    mockEvent({ proofMode: 'ReplyEmail' })
    mockSubmit(() => HttpResponse.json(result({ proofMode: 'ReplyEmail' }), { status: 201 }))
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('group', { name: 'Ticket' })
    await fillBuyer(user)
    await user.click(screen.getByRole('checkbox', { name: /Data Privacy Act/ }))
    await user.click(screen.getByRole('button', { name: 'Register & get billing details' }))
    expect(await screen.findByText(/then reply to the billing email with your proof of payment/)).toBeInTheDocument()
    expect(screen.queryByText(/upload your proof of payment/)).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Upload proof of payment' })).not.toBeInTheDocument()
  })

  it('asks for one card per extra group member and sends every attendee', async () => {
    mockEvent()
    const posts = mockSubmit(() =>
      HttpResponse.json(result({ participantIds: ['HTN-AAAA-0001', 'HTN-AAAA-0002', 'HTN-AAAA-0003', 'HTN-AAAA-0004', 'HTN-AAAA-0005'], amountDue: 6000 }), { status: 201 }),
    )
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('radio', { name: /Team of 5/ }))
    expect(screen.getByText(/Add 4 teammates on the next step/)).toBeInTheDocument()
    // Step 1 has no consent or submit yet.
    expect(screen.queryByRole('button', { name: 'Register & get billing details' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Continue · add 4 teammates' }))
    expect(await screen.findByText('Enter the first name.')).toBeInTheDocument()
    await fillBuyer(user)
    await user.click(screen.getByRole('button', { name: 'Continue · add 4 teammates' }))

    expect(await screen.findByRole('heading', { name: 'Your teammates' })).toBeInTheDocument()
    expect(screen.getByText('Step 2 of 2 · Your teammates')).toBeInTheDocument()
    const cards = screen.getAllByRole('group', { name: /^Teammate \d of 4$/ })
    expect(cards).toHaveLength(4)

    await user.click(screen.getByRole('checkbox', { name: /Data Privacy Act/ }))
    await user.click(screen.getByRole('button', { name: 'Register & get billing details' }))
    expect(await within(cards[0]).findByText('Enter the first name.')).toBeInTheDocument()
    expect(posts).toHaveLength(0)

    const teammates = ['Bea', 'Carl', 'Dani', 'Eli']
    for (const [i, card] of cards.entries()) {
      await user.type(within(card).getByLabelText('First name'), teammates[i])
      await user.type(within(card).getByLabelText('Last name'), 'Acme')
      await user.type(within(card).getByLabelText('Email address'), `dev${i + 2}@acmedev.ph`)
    }
    expect(screen.getByText('Team of 5 ticket')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Register & get billing details' }))
    await screen.findByRole('heading', { name: "You're registered" })
    expect(screen.getByText('Participant IDs')).toBeInTheDocument()
    expect(posts[0].body.ticketTypeId).toBe('t3')
    expect(posts[0].body.attendees).toHaveLength(5)
    expect(posts[0].body.attendees[4]).toEqual({ firstName: 'Eli', lastName: 'Acme', email: 'dev5@acmedev.ph' })
  })

  it('shows a duplicate group email on that teammate and does not submit', async () => {
    mockEvent()
    const posts = mockSubmit()
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('radio', { name: /Team of 5/ }))
    await fillBuyer(user)
    await user.click(screen.getByRole('button', { name: 'Continue · add 4 teammates' }))
    const cards = await screen.findAllByRole('group', { name: /^Teammate \d of 4$/ })
    const emails = ['bea@acmedev.ph', 'Maria.Santos@gmail.com', 'carl@acmedev.ph', 'dani@acmedev.ph']
    for (const [i, card] of cards.entries()) {
      await user.type(within(card).getByLabelText('First name'), ['Bea', 'Carl', 'Dani', 'Eli'][i])
      await user.type(within(card).getByLabelText('Last name'), 'Acme')
      await user.type(within(card).getByLabelText('Email address'), emails[i])
    }
    await user.click(screen.getByRole('checkbox', { name: /Data Privacy Act/ }))
    await user.click(screen.getByRole('button', { name: 'Register & get billing details' }))
    const message = 'Each person in the group needs their own email address.'
    expect(await within(cards[1]).findByText(message)).toBeInTheDocument()
    expect(within(cards[1]).getByLabelText('Email address')).toHaveAttribute('aria-invalid', 'true')
    expect(within(cards[0]).queryByText(message)).not.toBeInTheDocument()
    expect(posts).toHaveLength(0)
  })

  it('applies a voucher and shows the same generic message for any invalid code', async () => {
    mockEvent()
    const checks: unknown[] = []
    server.use(
      http.post('/api/public/events/htn-con-2026/voucher-check', async ({ request }) => {
        const body = (await request.json()) as { code: string }
        checks.push(body)
        return HttpResponse.json(body.code === 'STUDENT20' ? { valid: true, discount: 300, total: 1200 } : { valid: false, discount: 0, total: 1500 })
      }),
    )
    const user = userEvent.setup()
    renderPage()
    const code = await screen.findByLabelText(/Voucher code/)
    await user.type(code, 'expired1')
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    expect(await screen.findByText("This voucher code can't be used.")).toBeInTheDocument()
    expect(checks[0]).toEqual({ code: 'EXPIRED1', ticketTypeId: 't1' })

    await user.clear(code)
    expect(screen.queryByText("This voucher code can't be used.")).not.toBeInTheDocument()
    await user.type(code, 'student20{Enter}')
    expect(await screen.findByText('STUDENT20 applied · ₱300 off')).toBeInTheDocument()
    expect(screen.getByText('−₱300')).toBeInTheDocument()
    expect(screen.getByText('₱1,200')).toBeInTheDocument()
  })

  it('shows sold_out from the server and refreshes the tickets', async () => {
    let calls = 0
    server.use(
      http.get('/api/public/events/htn-con-2026', () => {
        calls += 1
        return HttpResponse.json(publicEvent())
      }),
    )
    mockSubmit(() => problem(409, 'Conflict', 'sold_out'))
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('group', { name: 'Ticket' })
    await fillBuyer(user)
    await user.click(screen.getByRole('checkbox', { name: /Data Privacy Act/ }))
    await user.click(screen.getByRole('button', { name: 'Register & get billing details' }))
    expect(await screen.findByText('This ticket just sold out. Pick another ticket.')).toBeInTheDocument()
    await waitFor(() => expect(calls).toBe(2))
  })

  it('puts invalid_voucher from the server on the voucher field', async () => {
    mockEvent()
    mockSubmit(() => problem(400, 'Bad Request', 'invalid_voucher'))
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole('group', { name: 'Ticket' })
    await fillBuyer(user)
    await user.type(screen.getByLabelText(/Voucher code/), 'PARTNER300')
    await user.click(screen.getByRole('checkbox', { name: /Data Privacy Act/ }))
    await user.click(screen.getByRole('button', { name: 'Register & get billing details' }))
    expect(await screen.findByText("This voucher code can't be used.")).toBeInTheDocument()
  })

  it('says registration is not open on 404', async () => {
    server.use(http.get('/api/public/events/htn-con-2026', () => problem(404, 'Not Found')))
    renderPage()
    expect(await screen.findByText("Registration isn't open")).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Register & get billing details' })).not.toBeInTheDocument()
  })
})

/** Exact-text matcher for long strings. */
function mockText(text: string) {
  return (_: string, el: Element | null) => el?.textContent === text && el.tagName === 'SPAN'
}
