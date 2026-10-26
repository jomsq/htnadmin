import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { boardMe, server, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { CreateAccountDialog } from './components/CreateAccountDialog'

const created = {
  id: 'n1',
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
}

function setup() {
  signInAs(boardMe)
  const onClose = vi.fn()
  renderWithProviders(<CreateAccountDialog open onClose={onClose} />)
  return { onClose, user: userEvent.setup() }
}

describe('CreateAccountDialog', () => {
  it('shows validation errors and does not submit', async () => {
    let called = false
    server.use(
      http.post('/api/people', () => {
        called = true
        return HttpResponse.json(created, { status: 201 })
      }),
    )
    const { user } = setup()
    const dialog = await screen.findByRole('dialog', { name: 'Create account' })
    await user.click(within(dialog).getByRole('button', { name: 'Create & send invite' }))
    expect(await within(dialog).findByText('Enter a first name.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter a last name.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter a valid email address.')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('First name')).toHaveAttribute('aria-invalid', 'true')
    expect(called).toBe(false)
  })

  it('submits the right body, toasts and closes', async () => {
    let body: unknown
    let csrf: string | null = null
    server.use(
      http.post('/api/people', async ({ request }) => {
        body = await request.json()
        csrf = request.headers.get('X-CSRF-TOKEN')
        return HttpResponse.json(created, { status: 201 })
      }),
    )
    const { user, onClose } = setup()
    const dialog = await screen.findByRole('dialog', { name: 'Create account' })
    await user.type(within(dialog).getByLabelText('First name'), 'Ana')
    await user.type(within(dialog).getByLabelText('Last name'), 'Bautista')
    await user.type(within(dialog).getByLabelText('Email'), 'ana.b@gmail.com')
    await user.type(within(dialog).getByLabelText(/Position/), 'Logistics')
    await user.click(within(dialog).getByRole('radio', { name: 'Goon' }))
    await user.click(within(dialog).getByRole('button', { name: 'Create & send invite' }))

    expect(await screen.findByText('Account created. Invite email sent; it expires in 48 hours.')).toBeInTheDocument()
    expect(body).toEqual({ firstName: 'Ana', lastName: 'Bautista', email: 'ana.b@gmail.com', position: 'Logistics', level: 'Goon' })
    expect(csrf).not.toBeNull()
    expect(onClose).toHaveBeenCalled()
  })

  it('uses "Add to roster" and the roster-only note for Member', async () => {
    const { user } = setup()
    const dialog = await screen.findByRole('dialog', { name: 'Create account' })
    expect(within(dialog).getByRole('radio', { name: 'Apprentice' })).toHaveAttribute('aria-checked', 'true')
    await user.click(within(dialog).getByRole('radio', { name: 'Member' }))
    expect(within(dialog).getByRole('button', { name: 'Add to roster' })).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Create & send invite' })).not.toBeInTheDocument()
    expect(within(dialog).getByText(/No login, roster only/)).toBeInTheDocument()
  })

  it('shows server validation errors on the matching field', async () => {
    server.use(
      http.post('/api/people', () =>
        HttpResponse.json(
          { title: 'One or more validation errors occurred.', status: 400, errors: { Email: ['This email is already in use.'] } },
          { status: 400 },
        ),
      ),
    )
    const { user } = setup()
    const dialog = await screen.findByRole('dialog', { name: 'Create account' })
    await user.type(within(dialog).getByLabelText('First name'), 'Ana')
    await user.type(within(dialog).getByLabelText('Last name'), 'Bautista')
    await user.type(within(dialog).getByLabelText('Email'), 'ana.b@gmail.com')
    await user.click(within(dialog).getByRole('button', { name: 'Create & send invite' }))
    expect(await within(dialog).findByText('This email is already in use.')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Email')).toHaveAttribute('aria-invalid', 'true')
  })
})
