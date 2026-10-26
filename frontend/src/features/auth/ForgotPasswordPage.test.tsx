import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { problem, server } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { ForgotPasswordPage, RESET_SENT_MESSAGE } from './ForgotPasswordPage'

async function submit() {
  const user = userEvent.setup()
  await user.type(await screen.findByLabelText('Email'), 'alex@hackthenorth.ph')
  await user.click(screen.getByRole('button', { name: 'Send reset link' }))
}

describe('ForgotPasswordPage', () => {
  it('shows the same confirmation on 202', async () => {
    server.use(http.post('/api/auth/password/forgot', () => new HttpResponse(null, { status: 202 })))
    renderWithProviders(<ForgotPasswordPage />)
    await submit()
    expect(await screen.findByText(RESET_SENT_MESSAGE)).toBeInTheDocument()
  })

  it('shows the rate-limit message on 429', async () => {
    server.use(http.post('/api/auth/password/forgot', () => problem(429, 'Too many attempts', 'rate_limited')))
    renderWithProviders(<ForgotPasswordPage />)
    await submit()
    expect(await screen.findByRole('alert')).toHaveTextContent('Too many attempts. Please wait a minute and try again.')
    expect(screen.queryByText(RESET_SENT_MESSAGE)).not.toBeInTheDocument()
  })
})
