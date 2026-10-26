import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { RedirectIfSignedIn } from '@/app/auth/RequireAuth'
import { boardMe, server } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { LoginPage } from './LoginPage'

function renderLogin(path = '/login') {
  return renderRoutes(
    [
      { element: <RedirectIfSignedIn />, children: [{ path: '/login', element: <LoginPage /> }] },
      { path: '/', element: <h1>Home</h1> },
      { path: '/people', element: <h1>People page</h1> },
      { path: '/setup-2fa', element: <h1>Setup 2FA</h1> },
    ],
    path,
  )
}

async function fillAndSubmit(email = 'alex@hackthenorth.ph', password = 'correct horse battery') {
  const user = userEvent.setup()
  await user.type(await screen.findByLabelText('Email'), email)
  await user.type(screen.getByLabelText('Password'), password)
  await user.click(screen.getByRole('button', { name: 'Sign in' }))
  return user
}

describe('LoginPage', () => {
  it('shows the sign-in copy and no public sign-up', async () => {
    renderLogin()
    expect(await screen.findByRole('heading', { name: 'Sign in to HTN Admin' })).toBeInTheDocument()
    expect(screen.getByText(/No account\? Accounts are created by the board/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Forgot password?' })).toHaveAttribute('href', '/forgot')
    expect(screen.queryByText(/sign up/i)).not.toBeInTheDocument()
  })

  it('signs in and redirects to the next path', async () => {
    let body: unknown
    server.use(
      http.post('/api/auth/login', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(boardMe)
      }),
    )
    renderLogin('/login?next=%2Fpeople')
    await fillAndSubmit()
    expect(await screen.findByRole('heading', { name: 'People page' })).toBeInTheDocument()
    expect(body).toEqual({ email: 'alex@hackthenorth.ph', password: 'correct horse battery', rememberMe: false })
  })

  it('ignores an off-site next parameter', async () => {
    server.use(http.post('/api/auth/login', () => HttpResponse.json(boardMe)))
    renderLogin('/login?next=%2F%2Fevil.example')
    await fillAndSubmit()
    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument()
  })

  it('shows an error on 401', async () => {
    server.use(http.post('/api/auth/login', () => HttpResponse.json({ title: 'Unauthorized', status: 401 }, { status: 401 })))
    renderLogin()
    await fillAndSubmit('alex@hackthenorth.ph', 'wrong password')
    expect(await screen.findByRole('alert')).toHaveTextContent('Email or password is incorrect.')
    expect(screen.getByRole('heading', { name: 'Sign in to HTN Admin' })).toBeInTheDocument()
  })

  it('validates the form before calling the API', async () => {
    let called = false
    server.use(
      http.post('/api/auth/login', () => {
        called = true
        return HttpResponse.json(boardMe)
      }),
    )
    renderLogin()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toHaveAttribute('aria-invalid', 'true')
    expect(called).toBe(false)
  })

  it('asks for a code on 202 and completes with /login/2fa', async () => {
    let code: unknown
    server.use(
      http.post('/api/auth/login', () => HttpResponse.json({ requires2fa: true }, { status: 202 })),
      http.post('/api/auth/login/2fa', async ({ request }) => {
        code = await request.json()
        return HttpResponse.json(boardMe)
      }),
    )
    renderLogin()
    const user = await fillAndSubmit()
    expect(await screen.findByRole('heading', { name: 'Enter your code' })).toBeInTheDocument()
    await user.type(screen.getByLabelText('Authentication code'), '123456')
    await user.click(screen.getByRole('button', { name: 'Verify' }))
    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument()
    expect(code).toEqual({ code: '123456' })
  })

  it('shows an error when the 2FA code is rejected', async () => {
    server.use(
      http.post('/api/auth/login', () => HttpResponse.json({ requires2fa: true }, { status: 202 })),
      http.post('/api/auth/login/2fa', () => HttpResponse.json({ title: 'Unauthorized', status: 401 }, { status: 401 })),
    )
    renderLogin()
    const user = await fillAndSubmit()
    await user.type(await screen.findByLabelText('Authentication code'), '000000')
    await user.click(screen.getByRole('button', { name: 'Verify' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('That code did not work.')
  })

  it('sends a board user without 2FA to /setup-2fa', async () => {
    server.use(http.post('/api/auth/login', () => HttpResponse.json({ ...boardMe, twoFactorEnabled: false })))
    renderLogin()
    await fillAndSubmit()
    expect(await screen.findByRole('heading', { name: 'Setup 2FA' })).toBeInTheDocument()
  })
})
