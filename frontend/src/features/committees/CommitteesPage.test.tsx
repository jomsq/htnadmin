import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { routes } from '@/app/router'
import { committee, committeesPage } from '@/test/fixtures/committees'
import { detail } from '@/test/fixtures/events'
import { boardMe, goonMe, problem, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'

type Json = Record<string, unknown>

/** The screen inside its real route (EventLayout + tabs), as a user opens it. */
function renderPage(path = '/events/e1/committees') {
  const user = userEvent.setup()
  return { user, ...renderRoutes(routes, path) }
}

function mockApi(page: Json = committeesPage()) {
  let calls = 0
  server.use(
    http.get('/api/events/e1', () => HttpResponse.json(detail())),
    http.get('/api/events/e1/committees', () => {
      calls++
      return HttpResponse.json(page)
    }),
  )
  return () => calls
}

async function card(name: string) {
  const heading = await screen.findByRole('heading', { level: 3, name: new RegExp(name) })
  return heading.closest('section') as HTMLElement
}

describe('Committees', () => {
  it('shows the Committees tab and the cards with lead, members, volunteers and counts', async () => {
    signInAs(boardMe)
    mockApi()
    renderPage()

    const tabs = await screen.findByRole('navigation', { name: 'Event sections' })
    expect(within(tabs).getAllByRole('link').map((a) => a.textContent)).toEqual([
      'Details',
      'Committees',
      'Tasks',
      'Registrations',
      'Emails',
      'Check-in',
    ])
    expect(within(tabs).getByRole('link', { name: 'Committees' })).toHaveAttribute('aria-current', 'page')

    const logistics = await card('Logistics')
    expect(within(logistics).getByText('4 people · 9 open tasks')).toBeInTheDocument()
    expect(within(logistics).getByText('Mika Soriano')).toBeInTheDocument()
    expect(within(logistics).getByText('Committee lead · can assign & score tasks')).toBeInTheDocument()
    expect(within(logistics).getByText('Ana Bautista')).toBeInTheDocument()
    expect(within(logistics).getByText('Apprentice')).toBeInTheDocument()
    expect(within(logistics).getByText('Carla Diaz')).toBeInTheDocument()
    expect(within(logistics).getByText('Volunteer')).toBeInTheDocument()
    expect(within(logistics).getByRole('link', { name: 'Logistics task board' })).toHaveAttribute(
      'href',
      '/events/e1/tasks?committee=c-logistics',
    )

    // No lead yet state with its own action.
    const programs = await card('Programs')
    expect(within(programs).getByText('No lead yet')).toBeInTheDocument()
    expect(within(programs).getByRole('button', { name: 'Assign lead' })).toBeInTheDocument()

    const aside = screen.getByRole('complementary', { name: 'Not yet assigned' })
    expect(within(aside).getByText('Ken Morales')).toBeInTheDocument()
    expect(within(aside).getByText('Pia Santos')).toBeInTheDocument()
    expect(within(aside).getByRole('button', { name: 'Assign Ken Morales' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ New committee' })).toBeInTheDocument()
  })

  it('shows a loading state, then an error with retry', async () => {
    signInAs(boardMe)
    server.use(
      http.get('/api/events/e1', () => HttpResponse.json(detail())),
      http.get('/api/events/e1/committees', () => problem(500, 'Server error')),
    )
    renderPage()
    expect(await screen.findByText(/Could not load the committees/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })

  it('shows the empty state when the event has no committees', async () => {
    signInAs(boardMe)
    mockApi(committeesPage({ committees: [] }))
    renderPage()
    expect(await screen.findByText('No committees yet')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: '+ New committee' }).length).toBe(2)
  })

  it('adds a member with the right body and refetches the list', async () => {
    signInAs(boardMe)
    let body: Json | null = null
    const calls = mockApi()
    server.use(
      http.post('/api/events/e1/committees/c-logistics/members', async ({ request }) => {
        body = (await request.json()) as Json
        return HttpResponse.json(committee())
      }),
    )
    const { user } = renderPage()

    await user.click(within((await card('Logistics'))).getByRole('button', { name: '+ Add goon, apprentice or volunteer' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('heading', { name: 'Add to Logistics' })).toBeInTheDocument()
    const before = calls()
    await user.click(within(dialog).getByRole('button', { name: 'Add Ken Morales to Logistics' }))

    await waitFor(() => expect(body).toEqual({ personId: 'p-ken' }))
    expect(await within(dialog).findByText('Ken Morales is on Logistics.')).toBeInTheDocument()
    await waitFor(() => expect(calls()).toBeGreaterThan(before))
  })

  it('filters the people picker by level and lists volunteers separately', async () => {
    signInAs(boardMe)
    mockApi()
    const { user } = renderPage()
    await user.click(within((await card('Logistics'))).getByRole('button', { name: '+ Add goon, apprentice or volunteer' }))
    const dialog = await screen.findByRole('dialog')

    await user.click(within(dialog).getByRole('button', { name: 'Goons' }))
    expect(within(dialog).getByRole('button', { name: 'Add Ken Morales to Logistics' })).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Add Celine Villanueva to Logistics' })).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole('radio', { name: 'Volunteers' }))
    expect(within(dialog).getByRole('button', { name: 'Add Pia Santos to Logistics' })).toBeInTheDocument()
  })

  it('assigns someone from the pool to a chosen committee', async () => {
    signInAs(boardMe)
    let body: Json | null = null
    mockApi()
    server.use(
      http.post('/api/events/e1/committees/c-programs/members', async ({ request }) => {
        body = (await request.json()) as Json
        return HttpResponse.json(committee())
      }),
    )
    const { user } = renderPage()

    const aside = await screen.findByRole('complementary', { name: 'Not yet assigned' })
    await user.click(within(aside).getByRole('button', { name: 'Assign Celine Villanueva' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Committee'), 'c-programs')
    await user.click(within(dialog).getByRole('button', { name: 'Assign' }))

    await waitFor(() => expect(body).toEqual({ personId: 'p-celine' }))
    expect(await screen.findByText('Celine Villanueva is on Programs.')).toBeInTheDocument()
  })

  it('adds a volunteer with no login (name required, contact optional)', async () => {
    signInAs(boardMe)
    let body: Json | null = null
    mockApi()
    server.use(
      http.post('/api/events/e1/volunteers', async ({ request }) => {
        body = (await request.json()) as Json
        return HttpResponse.json({ id: 'v-new', name: 'Owen Reyes', email: null, mobile: null }, { status: 201 })
      }),
    )
    const { user } = renderPage()

    await user.click(await screen.findByRole('button', { name: '+ Add volunteer (no login)' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Add volunteer' }))
    expect(await within(dialog).findByText('Enter the volunteer’s name.')).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText('Name'), 'Owen Reyes')
    await user.selectOptions(within(dialog).getByLabelText(/Committee/), 'c-logistics')
    await user.click(within(dialog).getByRole('button', { name: 'Add volunteer' }))

    await waitFor(() => expect(body).toEqual({ name: 'Owen Reyes', committeeId: 'c-logistics' }))
    expect(await screen.findByText('Owen Reyes is added as a volunteer.')).toBeInTheDocument()
  })

  it('validates the new committee name and shows duplicate_name on the field', async () => {
    signInAs(boardMe)
    mockApi()
    server.use(http.post('/api/events/e1/committees', () => problem(409, 'Conflict', 'duplicate_name')))
    const { user } = renderPage()

    await user.click(await screen.findByRole('button', { name: '+ New committee' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Create committee' }))
    expect(await within(dialog).findByText('Enter a committee name.')).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText('Committee name'), 'Logistics')
    await user.click(within(dialog).getByRole('button', { name: 'Create committee' }))
    expect(await within(dialog).findByText('Another committee in this event already uses this name.')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Committee name')).toHaveAttribute('aria-invalid', 'true')
  })

  it('lists only goons in the lead picker and shows wrong_level from the server', async () => {
    signInAs(boardMe)
    mockApi()
    server.use(http.put('/api/events/e1/committees/c-programs/lead', () => problem(400, 'Bad Request', 'wrong_level')))
    const { user } = renderPage()

    await user.click(within((await card('Programs'))).getByRole('button', { name: 'Assign lead' }))
    const dialog = await screen.findByRole('dialog')
    const select = within(dialog).getByLabelText('Committee lead')
    const options = within(select).getAllByRole('option').map((o) => o.textContent)
    expect(options).toEqual(['Pick a goon', 'Bea Lopez', 'Ken Morales'])

    await user.selectOptions(select, 'p-ken')
    await user.click(within(dialog).getByRole('button', { name: 'Assign lead' }))
    expect(await within(dialog).findByText("This person's level doesn't fit that role.")).toBeInTheDocument()
  })

  it('requires a reason to deactivate and shows has_open_tasks', async () => {
    signInAs(boardMe)
    mockApi()
    server.use(http.post('/api/events/e1/committees/c-logistics/deactivate', () => problem(409, 'Conflict', 'has_open_tasks')))
    const { user } = renderPage()

    await user.click(within((await card('Logistics'))).getByRole('button', { name: 'Deactivate' }))
    const dialog = await screen.findByRole('alertdialog')
    await user.click(within(dialog).getByRole('button', { name: 'Deactivate committee' }))
    expect(await within(dialog).findByText('Enter a reason.')).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText('Reason'), 'Merged into Programs')
    await user.click(within(dialog).getByRole('button', { name: 'Deactivate committee' }))
    expect(await within(dialog).findByText('This committee still has open tasks. Move or finish them first.')).toBeInTheDocument()
  })

  it('gives a goon manage actions only on the committee they lead', async () => {
    signInAs(goonMe)
    mockApi(
      committeesPage({
        canCreateCommittee: false,
        canManageVolunteers: true,
        committees: [
          committee({ canManage: true }),
          committee({ id: 'c-programs', name: 'Programs', canManage: false, lead: null, members: [], volunteers: [] }),
        ],
      }),
    )
    renderPage()

    // Membership on their own committee, but no structural actions anywhere (Board only).
    const logistics = await card('Logistics')
    expect(within(logistics).getByRole('button', { name: '+ Add goon, apprentice or volunteer' })).toBeInTheDocument()
    expect(within(logistics).getByRole('button', { name: /Remove Ana Bautista/ })).toBeInTheDocument()
    expect(within(logistics).queryByRole('button', { name: 'Change lead' })).not.toBeInTheDocument()
    expect(within(logistics).queryByRole('button', { name: 'Rename' })).not.toBeInTheDocument()
    expect(within(logistics).queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument()

    const programs = await card('Programs')
    expect(within(programs).queryByRole('button', { name: '+ Add goon, apprentice or volunteer' })).not.toBeInTheDocument()
    expect(within(programs).queryByRole('button', { name: 'Assign lead' })).not.toBeInTheDocument()
    expect(within(programs).queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ New committee' })).not.toBeInTheDocument()
  })

  it('is read-only for an apprentice with no committee of their own', async () => {
    signInAs({ ...goonMe, level: 'Apprentice', policies: ['Events.View'] })
    mockApi(
      committeesPage({
        canCreateCommittee: false,
        canManageVolunteers: false,
        committees: [committee({ canManage: false })],
      }),
    )
    renderPage()

    const logistics = await card('Logistics')
    expect(within(logistics).getByText('Mika Soriano')).toBeInTheDocument()
    expect(within(logistics).queryByRole('button', { name: '+ Add goon, apprentice or volunteer' })).not.toBeInTheDocument()
    expect(within(logistics).queryByRole('button', { name: 'Change lead' })).not.toBeInTheDocument()
    expect(within(logistics).queryByRole('button', { name: 'Rename' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ New committee' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Add volunteer (no login)' })).not.toBeInTheDocument()
    const aside = screen.getByRole('complementary', { name: 'Not yet assigned' })
    expect(within(aside).queryByRole('button', { name: 'Assign Ken Morales' })).not.toBeInTheDocument()
    // No Tasks tab either: the board needs Tasks.ViewBoard.
    const tabs = screen.getByRole('navigation', { name: 'Event sections' })
    expect(within(tabs).queryByRole('link', { name: 'Tasks' })).not.toBeInTheDocument()
  })
})
