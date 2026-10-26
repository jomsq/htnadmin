import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { routes } from '@/app/router'
import { leaderboard, myRow } from '@/test/fixtures/leaderboard'
import { boardMe, goonMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'

describe('Dashboard leaderboard card', () => {
  it('shows the caller’s quarter points and rank', async () => {
    signInAs(goonMe)
    const calls: string[] = []
    server.use(
      http.get('/api/leaderboard', ({ request }) => {
        calls.push(new URL(request.url).search)
        return HttpResponse.json(leaderboard({ me: myRow() }))
      }),
    )
    renderRoutes(routes, '/')

    expect(await screen.findByRole('heading', { level: 1, name: 'Dashboard' }, { timeout: 5000 })).toBeInTheDocument()
    const card = await screen.findByRole('heading', { level: 2, name: 'Leaderboard · this quarter' })
    const section = card.closest('section') as HTMLElement
    expect(section).toHaveTextContent('Your points62')
    expect(section).toHaveTextContent('Your rank#14')
    expect(section).toHaveTextContent('1 / 2 / 1 as lead / collab / assistant')
    expect(screen.getByRole('link', { name: 'Full board' })).toHaveAttribute('href', '/leaderboard')
    expect(calls).toEqual(['?scope=quarter'])
  })

  it('hides the card when the caller has no leaderboard row (board members)', async () => {
    signInAs(boardMe)
    server.use(http.get('/api/leaderboard', () => HttpResponse.json(leaderboard({ me: null }))))
    renderRoutes(routes, '/')

    expect(await screen.findByRole('heading', { level: 1, name: 'Dashboard' }, { timeout: 5000 })).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('heading', { level: 2, name: 'Upcoming events' })).toBeInTheDocument())
    expect(screen.queryByRole('heading', { name: 'Leaderboard · this quarter' })).not.toBeInTheDocument()
    expect(screen.queryByText('Your points')).not.toBeInTheDocument()
  })

  it('does not call the leaderboard without the policy', async () => {
    signInAs({ ...goonMe, policies: goonMe.policies.filter((p) => p !== 'Leaderboard.View') })
    let calls = 0
    server.use(
      http.get('/api/leaderboard', () => {
        calls++
        return HttpResponse.json(leaderboard({ me: myRow() }))
      }),
    )
    renderRoutes(routes, '/')

    expect(await screen.findByRole('heading', { level: 1, name: 'Dashboard' }, { timeout: 5000 })).toBeInTheDocument()
    expect(screen.queryByText('Your points')).not.toBeInTheDocument()
    expect(calls).toBe(0)
  })
})
