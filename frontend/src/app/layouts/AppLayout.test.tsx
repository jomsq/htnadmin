import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { RequireAuth } from '@/app/auth/RequireAuth'
import { boardMe, boardWithout2faMe, goonMe, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { AppLayout } from './AppLayout'

function renderShell(path = '/') {
  return renderRoutes(
    [
      { path: '/login', element: <h1>Login page</h1> },
      { element: <RequireAuth />, children: [{ element: <AppLayout />, children: [{ path: '*', element: <h1>Content</h1> }] }] },
      { path: '/setup-2fa', element: <h1>Setup 2FA</h1> },
    ],
    path,
  )
}

describe('AppLayout', () => {
  it('shows the canvas sidebar items in order, Finance with lock for board', async () => {
    signInAs(boardMe)
    renderShell()
    await screen.findByRole('heading', { name: 'Content' })
    const nav = screen.getAllByRole('navigation', { name: 'Main navigation' })[0]
    const labels = Array.from(nav.querySelectorAll('a')).map((a) => a.textContent)
    expect(labels).toEqual(['Dashboard', 'Organization', 'Events', 'Directory', 'Merch store', 'FinanceBoard only', 'My tasks', 'Leaderboard', 'Settings'])
    expect(screen.getByRole('img', { name: 'Board only' })).toBeInTheDocument()
    expect(screen.getByText('Board · Admin')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
  })

  it('hides Finance without Finance.View', async () => {
    signInAs(goonMe)
    renderShell()
    await screen.findByRole('heading', { name: 'Content' })
    expect(screen.queryByRole('link', { name: /Finance/ })).not.toBeInTheDocument()
  })

  it('redirects signed-out users to /login', async () => {
    const { router } = renderShell('/people')
    expect(await screen.findByRole('heading', { name: 'Login page' })).toBeInTheDocument()
    expect(router.state.location.search).toBe('?next=%2Fpeople')
  })

  it('sends a board user without 2FA to /setup-2fa', async () => {
    signInAs(boardWithout2faMe)
    renderShell('/people')
    expect(await screen.findByRole('heading', { name: 'Setup 2FA' })).toBeInTheDocument()
  })
})
