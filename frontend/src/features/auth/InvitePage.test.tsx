import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { problem, server } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { InvitePage } from './InvitePage'
import { LoginPage } from './LoginPage'

const invite = {
  email: 'ana.b@gmail.com',
  firstName: 'Ana',
  level: 'Apprentice',
  inviterName: 'Alex Reyes',
  expiresAt: '2026-10-28T08:00:00Z',
}

function renderInvite(token = 'tok-123') {
  return renderRoutes(
    [
      { path: '/invite/:token', element: <InvitePage /> },
      { path: '/login', element: <LoginPage /> },
    ],
    `/invite/${token}`,
  )
}

async function setPassword(password = 'a-long-new-password') {
  const user = userEvent.setup()
  await user.type(await screen.findByLabelText('Create a password'), password)
  await user.type(screen.getByLabelText('Confirm password'), password)
  await user.click(screen.getByRole('button', { name: 'Activate account' }))
}

describe('InvitePage', () => {
  it('shows the inviter, level and a read-only email for a valid token', async () => {
    let requested = ''
    server.use(
      http.get('/api/auth/invite/:token', ({ params }) => {
        requested = String(params.token)
        return HttpResponse.json(invite)
      }),
    )
    renderInvite()
    expect(await screen.findByText(/Invited by Alex Reyes as/)).toHaveTextContent(
      'Invited by Alex Reyes as Apprentice. This link works once and expires in 48 hours.',
    )
    const email = screen.getByLabelText('Email')
    expect(email).toHaveValue('ana.b@gmail.com')
    expect(email).toHaveAttribute('readonly')
    expect(screen.getByLabelText('Confirm password')).toBeInTheDocument()
    expect(requested).toBe('tok-123')
  })

  it('activates the account and returns to sign in', async () => {
    let body: unknown
    server.use(
      http.get('/api/auth/invite/:token', () => HttpResponse.json(invite)),
      http.post('/api/auth/invite/accept', async ({ request }) => {
        body = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderInvite()
    await setPassword()
    expect(await screen.findByText('Your account is ready. Sign in with your new password.')).toBeInTheDocument()
    expect(body).toEqual({ token: 'tok-123', password: 'a-long-new-password' })
  })

  it('shows the invalid state when the token is unknown, used or expired (404)', async () => {
    server.use(http.get('/api/auth/invite/:token', () => new HttpResponse(null, { status: 404 })))
    renderInvite('expired')
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'This invite link is invalid or has expired. Ask an admin to resend it.',
    )
    expect(screen.queryByLabelText('Create a password')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to sign in' })).toHaveAttribute('href', '/login')
  })

  it('shows the invalid state when accept answers invite_invalid', async () => {
    server.use(
      http.get('/api/auth/invite/:token', () => HttpResponse.json(invite)),
      http.post('/api/auth/invite/accept', () => problem(400, 'This invite link is invalid or has expired.', 'invite_invalid')),
    )
    renderInvite()
    await setPassword()
    expect(await screen.findByRole('alert')).toHaveTextContent('This invite link is invalid or has expired.')
    expect(screen.queryByLabelText('Create a password')).not.toBeInTheDocument()
  })

  it('shows the server password rule under the field', async () => {
    server.use(
      http.get('/api/auth/invite/:token', () => HttpResponse.json(invite)),
      http.post('/api/auth/invite/accept', () =>
        HttpResponse.json(
          { status: 400, title: 'One or more validation errors occurred.', errors: { password: ['Passwords must be at least 12 characters.'] } },
          { status: 400 },
        ),
      ),
    )
    renderInvite()
    await setPassword()
    expect(await screen.findByText('Passwords must be at least 12 characters.')).toBeInTheDocument()
    expect(screen.getByLabelText('Create a password')).toHaveAttribute('aria-invalid', 'true')
  })

  it('shows the rate-limit message on 429', async () => {
    server.use(
      http.get('/api/auth/invite/:token', () => HttpResponse.json(invite)),
      http.post('/api/auth/invite/accept', () => problem(429, 'Too many attempts', 'rate_limited')),
    )
    renderInvite()
    await setPassword()
    expect(await screen.findByRole('alert')).toHaveTextContent('Too many attempts. Please wait a minute and try again.')
  })
})
