import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { problem, server } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { LoginPage } from './LoginPage'
import { ResetPasswordPage } from './ResetPasswordPage'

function renderReset(path = '/reset/rtok') {
  return renderRoutes(
    [
      { path: '/reset/:token', element: <ResetPasswordPage /> },
      { path: '/login', element: <LoginPage /> },
    ],
    path,
  )
}

async function submit(password = 'a-long-new-password') {
  const user = userEvent.setup()
  await user.type(await screen.findByLabelText('New password'), password)
  await user.type(screen.getByLabelText('Confirm password'), password)
  await user.click(screen.getByRole('button', { name: 'Set new password' }))
}

describe('ResetPasswordPage', () => {
  it('works from /reset/:token without an email', async () => {
    let body: unknown
    server.use(
      http.post('/api/auth/password/reset', async ({ request }) => {
        body = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderReset()
    await submit()
    expect(await screen.findByText('Password updated. Sign in with your new password.')).toBeInTheDocument()
    expect(body).toEqual({ token: 'rtok', password: 'a-long-new-password' })
  })

  it('shows reset_invalid with a link to request a new one', async () => {
    server.use(http.post('/api/auth/password/reset', () => problem(400, 'This reset link is invalid or has expired.', 'reset_invalid')))
    renderReset()
    await submit()
    expect(await screen.findByRole('alert')).toHaveTextContent('This reset link is invalid or has expired')
    expect(screen.getByRole('link', { name: 'Request a new link' })).toHaveAttribute('href', '/forgot')
  })

  it('shows the rate-limit message on 429', async () => {
    server.use(http.post('/api/auth/password/reset', () => problem(429, 'Too many attempts', 'rate_limited')))
    renderReset()
    await submit()
    expect(await screen.findByRole('alert')).toHaveTextContent('Too many attempts. Please wait a minute and try again.')
  })
})
