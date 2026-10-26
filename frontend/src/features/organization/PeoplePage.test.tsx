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
  },
]

function mockPeople(record?: URL[]) {
  server.use(
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
    await user.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Deactivate account' }))
    expect(await screen.findByText("You can't deactivate your own account.")).toBeInTheDocument()
  })

  it('filters by level through the API', async () => {
    signInAs(boardMe)
    const calls: URL[] = []
    mockPeople(calls)
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await screen.findByText('Alex Reyes')
    await user.click(screen.getByRole('button', { name: 'Apprentices' }))
    expect(screen.getByRole('button', { name: 'Apprentices' })).toHaveAttribute('aria-pressed', 'true')
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

  it('confirms before deactivating', async () => {
    signInAs(boardMe)
    mockPeople()
    let deactivated = ''
    server.use(
      http.post('/api/people/:id/deactivate', ({ params }) => {
        deactivated = String(params.id)
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<PeoplePage />)
    await user.click(await screen.findByRole('button', { name: /Alex Reyes/ }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Person details' })).getByRole('button', { name: 'Deactivate' }))
    const confirm = await screen.findByRole('alertdialog', { name: 'Deactivate Alex Reyes?' })
    expect(deactivated).toBe('')
    await user.click(within(confirm).getByRole('button', { name: 'Deactivate account' }))
    expect(await screen.findByText('Alex Reyes was deactivated.')).toBeInTheDocument()
    expect(deactivated).toBe('p1')
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
