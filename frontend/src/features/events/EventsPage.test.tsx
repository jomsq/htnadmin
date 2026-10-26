import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { boardMe, goonMe, problem, server, signInAs } from '@/test/server'
import { summary } from '@/test/fixtures/events'
import { renderWithProviders } from '@/test/render'
import { EventsPage } from './EventsPage'

const byTab: Record<string, unknown[]> = {
  upcoming: [summary(), summary({ id: 'e2', slug: 'hack-night', name: 'Hack Night: Cloud', venue: 'Online', capacity: 150, sold: 150, isMainEvent: false })],
  drafts: [summary({ id: 'e3', slug: 'student-ctf', name: 'Student CTF', status: 'Draft', sold: 0, capacity: 200 })],
  past: [summary({ id: 'e4', name: 'Women in Security Meetup', status: 'Ended', sold: 120, capacity: 120 })],
}

function mockList() {
  const tabs: string[] = []
  server.use(
    http.get('/api/events', ({ request }) => {
      const tab = new URL(request.url).searchParams.get('tab') ?? ''
      tabs.push(tab)
      return HttpResponse.json(tab === 'all' ? Object.values(byTab).flat() : (byTab[tab] ?? []))
    }),
  )
  return tabs
}

describe('EventsPage', () => {
  it('shows upcoming event cards with status, sold/capacity and a progress bar', async () => {
    signInAs(boardMe)
    mockList()
    renderWithProviders(<EventsPage />, '/events')
    const list = await screen.findByRole('list', { name: 'Events' })
    const con = within(list).getByRole('link', { name: /HTN Con 2026/ })
    expect(con).toHaveAttribute('href', '/events/e1/details')
    expect(within(con).getByText('On sale')).toBeInTheDocument()
    expect(within(con).getByText('312 / 400')).toBeInTheDocument()
    expect(within(con).getByText(/Nov 22, 2026 · SMX Convention Center, Pasay/)).toBeInTheDocument()
    expect(within(con).getByRole('progressbar', { name: '78% of capacity sold' })).toHaveAttribute('aria-valuenow', '78')
    // On sale at capacity reads "Sold out".
    expect(within(within(list).getByRole('link', { name: /Hack Night/ })).getByText('Sold out')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '+ Create event' })).toHaveAttribute('href', '/events/new')
  })

  it('switches tabs: Upcoming, Drafts, Past, All', async () => {
    signInAs(boardMe)
    const tabs = mockList()
    const user = userEvent.setup()
    renderWithProviders(<EventsPage />, '/events')
    await screen.findByRole('button', { name: 'Drafts' }) // shown once /me says Events.Manage
    const group = screen.getByRole('group', { name: 'Show events' })
    expect(within(group).getAllByRole('button').map((b) => b.textContent)).toEqual(['Upcoming', 'Drafts', 'Past', 'All'])
    expect(within(group).getByRole('button', { name: 'Upcoming' })).toHaveAttribute('aria-pressed', 'true')

    await user.click(within(group).getByRole('button', { name: 'Drafts' }))
    expect(await screen.findByText('Student CTF')).toBeInTheDocument()
    expect(screen.getByText('Draft')).toBeInTheDocument()
    expect(within(group).getByRole('button', { name: 'Drafts' })).toHaveAttribute('aria-pressed', 'true')

    await user.click(within(group).getByRole('button', { name: 'Past' }))
    expect(await screen.findByText('Women in Security Meetup')).toBeInTheDocument()
    expect(screen.getByText('Ended')).toBeInTheDocument()

    await user.click(within(group).getByRole('button', { name: 'All' }))
    expect(await screen.findAllByRole('link', { name: /HTN Con 2026|Student CTF|Hack Night|Women in Security/ })).toHaveLength(4)
    expect(tabs).toEqual(['upcoming', 'drafts', 'past', 'all'])
  })

  it('hides Drafts and "+ Create event" from goons and never asks for drafts', async () => {
    signInAs(goonMe)
    const tabs = mockList()
    renderWithProviders(<EventsPage />, '/events?tab=drafts')
    expect(await screen.findByText('HTN Con 2026')).toBeInTheDocument()
    const group = screen.getByRole('group', { name: 'Show events' })
    expect(within(group).getAllByRole('button').map((b) => b.textContent)).toEqual(['Upcoming', 'Past', 'All'])
    expect(screen.queryByRole('link', { name: '+ Create event' })).not.toBeInTheDocument()
    expect(screen.queryByText('Student CTF')).not.toBeInTheDocument()
    expect(tabs).toEqual(['upcoming'])
  })

  it('shows an empty state with the create action for managers', async () => {
    signInAs(boardMe)
    server.use(http.get('/api/events', () => HttpResponse.json([])))
    renderWithProviders(<EventsPage />, '/events')
    expect(await screen.findByText('No upcoming events')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: '+ Create event' })).toHaveLength(2)
  })

  it('shows an error state with retry', async () => {
    signInAs(boardMe)
    server.use(http.get('/api/events', () => problem(500, 'Boom')))
    renderWithProviders(<EventsPage />, '/events')
    expect(await screen.findByText(/Could not load events/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})
