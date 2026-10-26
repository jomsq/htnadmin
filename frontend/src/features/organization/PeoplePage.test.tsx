import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { boardMe, goonMe, problem, server, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { PeoplePage } from './PeoplePage'

const people = [
  {
    id: 'p1',
    firstName: 'Alex',
    middleName: null,
    lastName: 'Reyes',
    email: 'alex@hackthenorth.ph',
    level: 'Board',
    position: 'Chairperson',
    isActive: true,
    accountStatus: 'Active',
    lastSignInAt: '2026-10-25T08:00:00Z',
    createdAt: '2025-10-01T08:00:00Z',
    isCfo: false,
    isFinanceAssistant: false,
  },
  {
    id: 'p6',
    firstName: 'Ana',
    middleName: null,
    lastName: 'Bautista',
    email: 'ana.b@gmail.com',
    level: 'Apprentice',
    position: 'Logistics',
    isActive: true,
    accountStatus: 'InvitePending',
    lastSignInAt: null,
    createdAt: '2026-10-24T08:00:00Z',
    isCfo: false,
    isFinanceAssistant: false,
  },
]

function mockPeople(record?: URL[], history: unknown[] = []) {
  server.use(
    http.get('/api/people/:id', ({ params }) => {
      const person = people.find((p) => p.id === params.id)
      if (!person) return problem(404, 'Not found')
      return HttpResponse.json({ person, positions: [], history })
    }),
    http.get('/api/people', ({ request }) => {
      const url = new URL(request.url)
      record?.push(url)
      const level = url.searchParams.get('level')
      const items = level ? people.filter((p) => p.level === level) : people
      return HttpResponse.json({ items, total: items.length, page: 1, pageSize: 25 })
    }),
  )
}

describe('PeoplePage', () => {
  it('lists people with level and account status', async () => {
    signInAs(boardMe)
    mockPeople()
    renderWithProviders(<PeoplePage />)
    expect(await screen.findByText('Alex Reyes')).toBeInTheDocument()
    const row = screen.getByText('Ana Bautista').closest('tr')
    expect(row).not.toBeNull()
    expect(within(row as HTMLElement).getByText('Apprentice')).toBeInTheDocument()
    expect(within(row as HTMLElement).getByText('Invite pending')).toBeInTheDocument()
  })

  it('shows "+ Create account" with People.Manage', async () => {
    signInAs(boardMe)
    mockPeople()
    renderWithProviders(<PeoplePage />)
    expect(await screen.findByRole('button', { name: '+ Create account' })).toBeInTheDocument()
  })

  it('hides "+ Create account" and account actions without People.Manage', async () => {
    signInAs(goonMe)
    mockPeople()
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Ana Bautista/ }))
    const drawer = await screen.findByRole('dialog', { name: 'Person details' })
    expect(within(drawer).getByText(/Invite sent/)).toBeInTheDocument()
    expect(within(drawer).queryByRole('button', { name: 'Resend invite' })).not.toBeInTheDocument()
    expect(within(drawer).queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Create account' })).not.toBeInTheDocument()
  })

  it('hides "Send password reset link" without People.Manage', async () => {
    signInAs(goonMe)
    mockPeople()
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Alex Reyes/ }))
    const drawer = await screen.findByRole('dialog', { name: 'Person details' })
    expect(within(drawer).getByText(/Active · last sign-in/)).toBeInTheDocument()
    expect(within(drawer).queryByRole('button', { name: 'Send password reset link' })).not.toBeInTheDocument()
  })

  it('offers "Send password reset link" only for Active accounts', async () => {
    signInAs(boardMe)
    mockPeople()
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Ana Bautista/ }))
    let drawer = await screen.findByRole('dialog', { name: 'Person details' })
    expect(within(drawer).getByRole('button', { name: 'Resend invite' })).toBeInTheDocument()
    expect(within(drawer).queryByRole('button', { name: 'Send password reset link' })).not.toBeInTheDocument()
    await user.keyboard('{Escape}')
    await user.click(await screen.findByRole('button', { name: /Alex Reyes/ }))
    drawer = await screen.findByRole('dialog', { name: 'Person details' })
    expect(within(drawer).getByRole('button', { name: 'Send password reset link' })).toBeInTheDocument()
    expect(within(drawer).queryByRole('button', { name: 'Resend invite' })).not.toBeInTheDocument()
  })

  it('sends a password reset link from the drawer', async () => {
    signInAs(boardMe)
    mockPeople()
    let sent = ''
    server.use(
      http.post('/api/people/:id/send-password-reset', ({ params }) => {
        sent = String(params.id)
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Alex Reyes/ }))
    const drawer = await screen.findByRole('dialog', { name: 'Person details' })
    await user.click(within(drawer).getByRole('button', { name: 'Send password reset link' }))
    expect(await screen.findByText('Password reset link sent to alex@hackthenorth.ph.')).toBeInTheDocument()
    expect(sent).toBe('p1')
  })

  it('shows the 409 reason in a toast', async () => {
    signInAs(boardMe)
    mockPeople()
    server.use(
      http.post('/api/people/:id/send-password-reset', () =>
        problem(409, "This account isn't active yet. Resend the invite instead.", 'not_active'),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Alex Reyes/ }))
    const drawer = await screen.findByRole('dialog', { name: 'Person details' })
    await user.click(within(drawer).getByRole('button', { name: 'Send password reset link' }))
    expect(await screen.findByText('Could not send a password reset link.')).toBeInTheDocument()
    expect(screen.getByText("This account isn't active yet. Resend the invite instead.")).toBeInTheDocument()
  })

  it('explains self_deactivate', async () => {
    signInAs(boardMe)
    mockPeople()
    server.use(http.post('/api/people/:id/deactivate', () => problem(409, "You can't deactivate your own account.", 'self_deactivate')))
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Alex Reyes/ }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Person details' })).getByRole('button', { name: 'Deactivate' }))
    const confirm = await screen.findByRole('alertdialog')
    await user.type(within(confirm).getByLabelText('Reason'), 'Testing')
    await user.click(within(confirm).getByRole('button', { name: 'Deactivate account' }))
    expect(await screen.findByText("You can't deactivate your own account.")).toBeInTheDocument()
  })

  it('filters by level through the API', async () => {
    signInAs(boardMe)
    const calls: URL[] = []
    mockPeople(calls)
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await screen.findByText('Alex Reyes')
    await user.click(screen.getByRole('button', { name: /^Apprentices/ }))
    expect(screen.getByRole('button', { name: /^Apprentices/ })).toHaveAttribute('aria-pressed', 'true')
    await screen.findByText('Ana Bautista')
    expect(await screen.findByText('Ana Bautista')).toBeInTheDocument()
    expect(calls.some((u) => u.searchParams.get('level') === 'Apprentice')).toBe(true)
  })

  it('resends an invite from the drawer', async () => {
    signInAs(boardMe)
    mockPeople()
    let resent = ''
    server.use(
      http.post('/api/people/:id/resend-invite', ({ params }) => {
        resent = String(params.id)
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Ana Bautista/ }))
    const drawer = await screen.findByRole('dialog', { name: 'Person details' })
    await user.click(within(drawer).getByRole('button', { name: 'Resend invite' }))
    expect(await screen.findByText('Invite re-sent to ana.b@gmail.com.')).toBeInTheDocument()
    expect(resent).toBe('p6')
  })

  it('confirms before deactivating and sends the reason', async () => {
    signInAs(boardMe)
    mockPeople()
    let deactivated = ''
    let body: unknown
    server.use(
      http.post('/api/people/:id/deactivate', async ({ params, request }) => {
        deactivated = String(params.id)
        body = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Alex Reyes/ }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Person details' })).getByRole('button', { name: 'Deactivate' }))
    const confirm = await screen.findByRole('alertdialog', { name: 'Deactivate Alex Reyes?' })
    await user.click(within(confirm).getByRole('button', { name: 'Deactivate account' }))
    expect(await within(confirm).findByText('Enter a reason. It is kept in their history.')).toBeInTheDocument()
    expect(deactivated).toBe('')
    await user.type(within(confirm).getByLabelText('Reason'), 'Stepped down')
    await user.click(within(confirm).getByRole('button', { name: 'Deactivate account' }))
    expect(await screen.findByText('Alex Reyes was deactivated.')).toBeInTheDocument()
    expect(deactivated).toBe('p1')
    expect(body).toEqual({ reason: 'Stepped down' })
  })

  it('shows counts on the level chips', async () => {
    signInAs(boardMe)
    mockPeople()
    renderWithProviders(<PeoplePage />)
    expect(await screen.findByRole('button', { name: /^All\W+2$/ })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: /^Apprentices\W+1$/ })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: /^Goons\W+0$/ })).toBeInTheDocument()
  })

  it('promotes a member to the next step on the path', async () => {
    signInAs(boardMe)
    people.push({ ...people[1], id: 'p9', firstName: 'Paolo', lastName: 'Cruz', email: 'paolo@gmail.com', level: 'Member', accountStatus: 'NoLogin' })
    mockPeople()
    let body: unknown
    server.use(
      http.post('/api/people/:id/promote', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ ...people[2], level: 'Apprentice', accountStatus: 'InvitePending' })
      }),
    )
    const user = userEvent.setup()
    try {
      renderWithProviders(<PeoplePage />)
      await user.click(await screen.findByRole('button', { name: /Paolo Cruz/ }))
      const drawer = await screen.findByRole('dialog', { name: 'Person details' })
      const path = within(drawer).getByRole('list', { name: 'Path progress' })
      expect(within(path).getByText('Member').closest('li')).toHaveAttribute('aria-current', 'step')
      await user.click(within(drawer).getByRole('button', { name: 'Promote to Apprentice' }))
      const dialog = await screen.findByRole('dialog', { name: 'Promote Paolo Cruz' })
      expect(within(dialog).getByText(/Creates their login and emails an invite/)).toBeInTheDocument()
      expect(within(dialog).queryByRole('radio', { name: 'Goon' })).not.toBeInTheDocument()
      await user.click(within(dialog).getByRole('button', { name: 'Promote to Apprentice' }))
      expect(await screen.findByText('Paolo Cruz is now an Apprentice.')).toBeInTheDocument()
      expect(body).toEqual({ toLevel: 'Apprentice', reason: null })
    } finally {
      people.pop()
    }
  })

  it('requires a reason to move someone down', async () => {
    signInAs(boardMe)
    mockPeople()
    let body: unknown
    server.use(
      http.post('/api/people/:id/promote', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ ...people[1], level: 'Member' })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Ana Bautista/ }))
    const drawer = await screen.findByRole('dialog', { name: 'Person details' })
    await user.click(within(drawer).getByRole('button', { name: 'Change level' }))
    const dialog = await screen.findByRole('dialog', { name: /Ana Bautista|Change level/ })
    await user.click(within(dialog).getByRole('radio', { name: 'Member' }))
    await user.click(within(dialog).getByRole('button', { name: 'Move to Member' }))
    expect(await within(dialog).findByText('Enter a reason. Moving someone down needs one.')).toBeInTheDocument()
    expect(body).toBeUndefined()
    await user.type(within(dialog).getByLabelText(/Reason/), 'Inactive this season')
    await user.click(within(dialog).getByRole('button', { name: 'Move to Member' }))
    expect(await screen.findByText('Ana Bautista is now a Member.')).toBeInTheDocument()
    expect(body).toEqual({ toLevel: 'Member', reason: 'Inactive this season' })
  })

  it('shows the history newest first with reasons', async () => {
    signInAs(boardMe)
    mockPeople(undefined, [
      { at: '2026-10-20T08:00:00Z', kind: 'LevelChanged', from: 'Member', to: 'Apprentice', reason: 'Ran registration', actorName: 'Alex Reyes' },
      { at: '2026-01-10T08:00:00Z', kind: 'Created', from: null, to: 'Member', reason: null, actorName: null },
    ])
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Ana Bautista/ }))
    const drawer = await screen.findByRole('dialog', { name: 'Person details' })
    expect(await within(drawer).findByText('Promoted from Member to Apprentice')).toBeInTheDocument()
    expect(within(drawer).getByText('Reason: Ran registration')).toBeInTheDocument()
    expect(within(drawer).getByText('Added as Member')).toBeInTheDocument()
  })

  it('hides Promote, Change level and Edit without People.Manage', async () => {
    signInAs(goonMe)
    mockPeople()
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Ana Bautista/ }))
    const drawer = await screen.findByRole('dialog', { name: 'Person details' })
    expect(await within(drawer).findByText('History is visible to the board only.')).toBeInTheDocument()
    expect(within(drawer).queryByRole('button', { name: /Promote/ })).not.toBeInTheDocument()
    expect(within(drawer).queryByRole('button', { name: 'Change level' })).not.toBeInTheDocument()
    expect(within(drawer).queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Import CSV' })).not.toBeInTheDocument()
  })

  it('maps email_locked to the email field when editing', async () => {
    signInAs(boardMe)
    mockPeople()
    server.use(http.put('/api/people/:id', () => problem(409, 'Email locked', 'email_locked')))
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Ana Bautista/ }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Person details' })).getByRole('button', { name: 'Edit' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit person' })
    const email = within(dialog).getByLabelText('Email')
    await user.clear(email)
    await user.type(email, 'ana.new@gmail.com')
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }))
    expect(await within(dialog).findByText('The email can only change before the person has activated their login.')).toBeInTheDocument()
    expect(email).toHaveAttribute('aria-invalid', 'true')
  })

  it('shows CFO and finance assistant badges', async () => {
    signInAs(boardMe)
    people[0] = { ...people[0], isCfo: true }
    try {
      mockPeople()
      renderWithProviders(<PeoplePage />)
      const row = (await screen.findByText('Alex Reyes')).closest('tr') as HTMLElement
      expect(within(row).getByText('CFO')).toBeInTheDocument()
    } finally {
      people[0] = { ...people[0], isCfo: false }
    }
  })

  it('shows an empty state', async () => {
    signInAs(boardMe)
    server.use(http.get('/api/people', () => HttpResponse.json({ items: [], total: 0, page: 1, pageSize: 25 })))
    renderWithProviders(<PeoplePage />)
    expect(await screen.findByText('No people yet')).toBeInTheDocument()
  })

  it('shows an error state with retry', async () => {
    signInAs(boardMe)
    server.use(http.get('/api/people', () => HttpResponse.json({ title: 'Server error', status: 500 }, { status: 500 })))
    renderWithProviders(<PeoplePage />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load people.')
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})
