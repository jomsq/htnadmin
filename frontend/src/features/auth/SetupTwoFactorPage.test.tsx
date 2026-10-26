import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http } from 'msw'
import { describe, expect, it } from 'vitest'
import { boardWithout2faMe, problem, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { SetupTwoFactorPage } from './SetupTwoFactorPage'

describe('SetupTwoFactorPage', () => {
  it('lets the user continue when 2FA is already on (409)', async () => {
    signInAs(boardWithout2faMe)
    server.use(http.post('/api/auth/2fa/setup', () => problem(409, 'Two-step verification is already on.', '2fa_already_enabled')))
    renderRoutes(
      [
        { path: '/setup-2fa', element: <SetupTwoFactorPage /> },
        { path: '/', element: <h1>Home</h1> },
      ],
      '/setup-2fa',
    )
    expect(await screen.findByRole('alert')).toHaveTextContent('Two-step verification is already on.')
    await userEvent.setup().click(screen.getByRole('button', { name: 'Continue' }))
    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument()
  })
})
