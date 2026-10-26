import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { routes } from '@/app/router'
import { leaderboard, leaderboardRows, myRow } from '@/test/fixtures/leaderboard'
import { boardMe, goonMe, problem, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'

type Json = Record<string, unknown>

/** Handler that records every leaderboard query string it answers. */
function handleLeaderboard(body: (url: URL) => Json) {
  const calls: string[] = []
  server.use(
    http.get('/api/leaderboard', ({ request }) => {
      const url = new URL(request.url)
      calls.push(url.search)
      return HttpResponse.json(body(url))
    }),
  )
  return calls
}

function renderLeaderboard(path = '/leaderboard') {
  const user = userEvent.setup()
  return { user, ...renderRoutes(routes, path) }
}

const findTable = () => screen.findByRole('table', undefined, { timeout: 5000 })

describe('Leaderboard', () => {
  it('renders the podium and the rankings table from the API', async () => {
    signInAs(boardMe)
    const calls = handleLeaderboard(() => leaderboard())
    renderLeaderboard()

    expect(await screen.findByRole('heading', { level: 1, name: 'Leaderboard' }, { timeout: 5000 })).toBeInTheDocument()
    expect(screen.getByText('Points from completed tasks. Goons and apprentices only.')).toBeInTheDocument()
    expect(calls).toEqual(['?scope=quarter'])
    const table = await findTable()

    // Podium: DOM order stays 1, 2, 3; the first place is moved to the middle visually.
    const podium = screen.getByRole('region', { name: 'Top three' })
    const places = within(podium).getAllByRole('listitem')
    expect(places).toHaveLength(3)
    expect(places.map((li) => li.textContent)).toEqual([
      expect.stringContaining('Mika Soriano'),
      expect.stringContaining('Rafael Tan'),
      expect.stringContaining('Jamie Lim'),
    ])
    expect(places[0].className).toContain('order-2')
    expect(places[1].className).toContain('order-1')
    expect(places[2].className).toContain('order-3')
    // Rank is written out, not only coloured.
    expect(within(places[0]).getByText('1')).toBeInTheDocument()
    expect(within(places[0]).getByText('1st place')).toBeInTheDocument()
    expect(within(places[0]).getByText('412 pts')).toBeInTheDocument()
    expect(within(places[0]).getByText('Goon · Logistics')).toBeInTheDocument()

    expect(within(table).getByText('Rankings · This quarter')).toBeInTheDocument()
    expect(within(table).getAllByRole('columnheader').map((th) => th.textContent)).toEqual([
      'Rank',
      'Person',
      'Committee',
      'Tasks done',
      'As lead / collab / assistant',
      'Points',
    ])
    expect(within(table).getAllByRole('rowheader')).toHaveLength(leaderboardRows().length)

    const row = within(table).getByRole('rowheader', { name: /Mika Soriano/ }).closest('tr') as HTMLElement
    expect(within(row).getByText('Goon')).toBeInTheDocument()
    expect(within(row).getByText('Logistics')).toBeInTheDocument()
    expect(within(row).getByText('18')).toBeInTheDocument()
    expect(within(row).getByText('9 / 6 / 3')).toBeInTheDocument()
    expect(within(row).getByText('412')).toBeInTheDocument()
    expect(within(table).getByRole('rowheader', { name: /Jamie Lim/ }).closest('tr')).toHaveTextContent('Apprentice')
  })

  it('keeps the scope in the URL and refetches when it changes', async () => {
    signInAs(boardMe)
    const calls = handleLeaderboard((url) =>
      url.searchParams.get('scope') === 'alltime' ? leaderboard({ scope: 'alltime', scopeLabel: 'All time' }) : leaderboard(),
    )
    const { user, router } = renderLeaderboard()
    await findTable()

    await user.click(screen.getByRole('radio', { name: 'All time' }))

    await waitFor(() => expect(router.state.location.search).toBe('?scope=alltime'))
    expect(await screen.findByText('Rankings · All time')).toBeInTheDocument()
    expect(calls).toEqual(['?scope=quarter', '?scope=alltime'])
  })

  it('filters by committee', async () => {
    signInAs(boardMe)
    const calls = handleLeaderboard((url) =>
      url.searchParams.get('committeeId') ? leaderboard({ committeeId: 'c-logistics', rows: [leaderboardRows()[0]] }) : leaderboard(),
    )
    const { user, router } = renderLeaderboard()
    await findTable()

    await user.selectOptions(screen.getByLabelText('Committee'), 'c-logistics')

    await waitFor(() => expect(router.state.location.search).toBe('?committeeId=c-logistics'))
    await waitFor(() => expect(calls).toEqual(['?scope=quarter', '?scope=quarter&committeeId=c-logistics']))
    await waitFor(() => expect(screen.getAllByRole('rowheader')).toHaveLength(1))
  })

  it('shows the event select and sends eventId for the event scope', async () => {
    signInAs(boardMe)
    const calls = handleLeaderboard((url) => {
      const eventId = url.searchParams.get('eventId')
      return eventId ? leaderboard({ scope: 'event', scopeLabel: 'HTN Con 2026', eventId }) : leaderboard()
    })
    const { user, router } = renderLeaderboard()
    await findTable()

    // The event segment carries the event's own name (canvas frame 14).
    await user.click(screen.getByRole('radio', { name: 'HTN Con 2026' }))

    await waitFor(() => expect(router.state.location.search).toBe('?scope=event&eventId=e1'))
    expect(calls).toEqual(['?scope=quarter', '?scope=event&eventId=e1'])
    const eventSelect = await screen.findByLabelText('Event')
    expect(eventSelect).toHaveValue('e1')

    await user.selectOptions(eventSelect, 'e2')
    await waitFor(() => expect(router.state.location.search).toBe('?scope=event&eventId=e2'))
    await waitFor(() => expect(calls).toContain('?scope=event&eventId=e2'))
  })

  it('opens a shared event link with the event already picked', async () => {
    signInAs(boardMe)
    const calls = handleLeaderboard(() => leaderboard({ scope: 'event', scopeLabel: 'HTN Summit 2025', eventId: 'e2' }))
    renderLeaderboard('/leaderboard?scope=event&eventId=e2')

    await findTable()
    expect(calls).toEqual(['?scope=event&eventId=e2'])
    expect(await screen.findByLabelText('Event')).toHaveValue('e2')
  })

  it('highlights the caller’s own row', async () => {
    signInAs(goonMe)
    const mine = leaderboardRows()[4]
    mine.isMe = true
    handleLeaderboard(() => leaderboard({ rows: leaderboardRows().map((r) => (r.personId === mine.personId ? mine : r)), me: mine }))
    renderLeaderboard()

    const table = await findTable()
    const row = within(table).getByRole('rowheader', { name: /Ana Bautista/ }).closest('tr') as HTMLElement
    expect(within(row).getByText('You')).toBeInTheDocument()
    expect(row.className).toContain('bg-brand-50')
    // Only the caller's row is marked.
    expect(within(table).getAllByText('You')).toHaveLength(1)
  })

  it('pins the caller’s row at the bottom when they are outside the visible list', async () => {
    signInAs(goonMe)
    handleLeaderboard(() => leaderboard({ me: myRow({ personId: 'p-me', name: 'Nina Villar', initials: 'NV' }) }))
    renderLeaderboard()

    const table = await findTable()
    const rowHeaders = within(table).getAllByRole('rowheader')
    expect(rowHeaders).toHaveLength(leaderboardRows().length + 1)
    const pinned = rowHeaders[rowHeaders.length - 1].closest('tr') as HTMLElement
    expect(pinned).toHaveTextContent('Nina Villar')
    expect(within(pinned).getByText('You')).toBeInTheDocument()
    expect(within(pinned).getByText('14')).toBeInTheDocument()
    expect(pinned.className).toContain('border-t-2')
  })

  it('shows an empty state when nobody has points yet', async () => {
    signInAs(boardMe)
    handleLeaderboard(() => leaderboard({ rows: [] }))
    renderLeaderboard()

    expect(await screen.findByText('No points yet', undefined, { timeout: 5000 })).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Top three' })).not.toBeInTheDocument()
  })

  it('shows an error state with retry', async () => {
    signInAs(boardMe)
    let calls = 0
    server.use(
      http.get('/api/leaderboard', () => {
        calls++
        return calls === 1 ? problem(500, 'Server error') : HttpResponse.json(leaderboard())
      }),
    )
    const { user } = renderLeaderboard()

    expect(await screen.findByText(/Could not load the leaderboard/, undefined, { timeout: 5000 })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await findTable()).toBeInTheDocument()
  })

  it('hides the sidebar link and the page from a user without Leaderboard.View', async () => {
    signInAs({ ...goonMe, policies: goonMe.policies.filter((p) => p !== 'Leaderboard.View') })
    renderLeaderboard()

    expect(await screen.findByText('You do not have access to this page', undefined, { timeout: 5000 })).toBeInTheDocument()
    const nav = screen.getAllByRole('navigation', { name: 'Main navigation' })[0]
    expect(within(nav).queryByRole('link', { name: /Leaderboard/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })
})
