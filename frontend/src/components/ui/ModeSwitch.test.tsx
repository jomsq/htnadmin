import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { boardMe, server, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { ModeSwitch } from './ModeSwitch'

function stubMatchMedia(dark: boolean) {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({
      matches: dark && query.includes('dark'),
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  )
}

afterEach(() => vi.unstubAllGlobals())

describe('ModeSwitch', () => {
  it('is an icon-only radiogroup with labels and tooltips', async () => {
    renderWithProviders(<ModeSwitch />)
    const dark = await screen.findByRole('radio', { name: 'Dark' })
    expect(dark).toHaveAttribute('title', 'Dark')
    expect(screen.getByRole('radio', { name: 'System' })).toHaveAttribute('aria-checked', 'true')
  })

  it('sets data-theme on <html> and remembers it before sign-in', async () => {
    const user = userEvent.setup()
    renderWithProviders(<ModeSwitch />)
    await user.click(await screen.findByRole('radio', { name: 'Dark' }))
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(JSON.parse(window.localStorage.getItem('htn.preferences') ?? '{}')).toMatchObject({ mode: 'dark' })
    await user.click(screen.getByRole('radio', { name: 'Light' }))
    expect(document.documentElement.dataset.theme).toBe('light')
  })

  it('System follows prefers-color-scheme', async () => {
    stubMatchMedia(true)
    renderWithProviders(<ModeSwitch />)
    await screen.findByRole('radio', { name: 'System' })
    expect(document.documentElement.dataset.theme).toBe('dark')
  })

  it('saves preferences to the API when signed in', async () => {
    signInAs(boardMe)
    let saved: unknown
    server.use(
      http.put('/api/me/preferences', async ({ request }) => {
        saved = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<ModeSwitch />)
    // wait for /me to load so the switch reflects the server preferences
    await waitFor(() => expect(document.documentElement.dataset.density).toBe('comfortable'))
    await user.click(await screen.findByRole('radio', { name: 'Dark' }))
    expect(document.documentElement.dataset.theme).toBe('dark')
    await waitFor(() => expect(saved).toEqual({ mode: 'dark', textSize: 'default', density: 'comfortable' }))
  })
})
