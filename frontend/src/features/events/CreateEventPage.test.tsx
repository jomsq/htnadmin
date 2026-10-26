import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { boardMe, problem, server, signInAs } from '@/test/server'
import { draftDetail } from '@/test/fixtures/events'
import { renderRoutes } from '@/test/render'
import { CreateEventPage } from './CreateEventPage'

function renderCreate() {
  return renderRoutes(
    [
      { path: '/events/new', element: <CreateEventPage /> },
      { path: '/events/:eventId/details', element: <h1>Details page</h1> },
    ],
    '/events/new',
  )
}

describe('CreateEventPage', () => {
  it('validates required fields, capacity and the end after the start', async () => {
    signInAs(boardMe)
    let calls = 0
    server.use(
      http.post('/api/events', () => {
        calls++
        return HttpResponse.json(draftDetail(), { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderCreate()
    await user.click(await screen.findByRole('button', { name: 'Create event' }))
    expect(await screen.findByText('Enter the event name.')).toBeInTheDocument()
    expect(screen.getByText('Enter the venue.')).toBeInTheDocument()
    expect(screen.getAllByText('Pick a date and time.')).toHaveLength(2)
    expect(screen.getByText('Enter a whole number from 1 to 100,000.')).toBeInTheDocument()
    expect(screen.getByLabelText('Event name')).toHaveAttribute('aria-invalid', 'true')

    await user.type(screen.getByLabelText('Event name'), 'Student CTF')
    await user.type(screen.getByLabelText(/Registration page link/), 'Bad Slug!')
    await user.type(screen.getByLabelText('Start'), '2027-01-17T09:00')
    await user.type(screen.getByLabelText('End'), '2027-01-17T08:00')
    await user.type(screen.getByLabelText('Venue'), 'UP Diliman, Quezon City')
    await user.type(screen.getByLabelText('Capacity'), '0')
    await user.click(screen.getByRole('button', { name: 'Create event' }))
    expect(await screen.findByText('The end must be after the start.')).toBeInTheDocument()
    expect(screen.getByText('Use 3 to 60 lower-case letters, numbers and single dashes.')).toBeInTheDocument()
    expect(screen.getByText('Enter a whole number from 1 to 100,000.')).toBeInTheDocument()
    expect(calls).toBe(0)
  })

  it('creates a draft with Manila times as ISO and opens its Details tab', async () => {
    signInAs(boardMe)
    let body: unknown
    server.use(
      http.post('/api/events', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(draftDetail(), { status: 201 })
      }),
    )
    const user = userEvent.setup()
    const { router } = renderCreate()
    await user.type(await screen.findByLabelText('Event name'), 'Student CTF')
    await user.type(screen.getByLabelText('Start'), '2027-01-17T09:00')
    await user.type(screen.getByLabelText('End'), '2027-01-17T18:00')
    await user.type(screen.getByLabelText('Venue'), 'UP Diliman, Quezon City')
    await user.type(screen.getByLabelText('Capacity'), '200')
    expect(screen.getByText(/Attendees register at \/r\/student-ctf/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Create event' }))
    expect(await screen.findByRole('heading', { name: 'Details page' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/events/e3/details')
    expect(body).toEqual({
      name: 'Student CTF',
      startsAt: '2027-01-17T09:00:00+08:00',
      endsAt: '2027-01-17T18:00:00+08:00',
      venue: 'UP Diliman, Quezon City',
      capacity: 200,
    })
    expect(screen.getByText('Student CTF was created as a draft.')).toBeInTheDocument()
  })

  it('shows duplicate_slug on the page link field', async () => {
    signInAs(boardMe)
    server.use(http.post('/api/events', () => problem(409, 'Conflict', 'duplicate_slug')))
    const user = userEvent.setup()
    renderCreate()
    await user.type(await screen.findByLabelText('Event name'), 'HTN Con 2026')
    await user.type(screen.getByLabelText(/Registration page link/), 'htn-con-2026')
    await user.type(screen.getByLabelText('Start'), '2026-11-22T08:00')
    await user.type(screen.getByLabelText('End'), '2026-11-22T18:00')
    await user.type(screen.getByLabelText('Venue'), 'SMX Convention Center, Pasay')
    await user.type(screen.getByLabelText('Capacity'), '400')
    await user.click(screen.getByRole('button', { name: 'Create event' }))
    expect(await screen.findByText('Another event already uses this page link. Pick a different one.')).toBeInTheDocument()
    expect(screen.getByLabelText(/Registration page link/)).toHaveAttribute('aria-invalid', 'true')
  })
})
