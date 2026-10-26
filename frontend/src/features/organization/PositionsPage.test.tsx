import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { boardMe, goonMe, problem, server, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { PositionsPage } from './PositionsPage'

const person = (id: string, firstName: string, lastName: string, level: string, accountStatus = 'Active') => ({
  id,
  firstName,
  middleName: null,
  lastName,
  email: `${firstName.toLowerCase()}@hackthenorth.ph`,
  level,
  position: null,
  isActive: true,
  accountStatus,
  lastSignInAt: null,
  createdAt: '2026-01-01T00:00:00Z',
  isCfo: false,
  isFinanceAssistant: false,
})

const roster = [
  person('p1', 'Alex', 'Reyes', 'Board'),
  person('p3', 'Bea', 'Lim', 'Board'),
  person('p2', 'Mika', 'Soriano', 'Goon'),
  person('p4', 'Jon', 'Tan', 'Goon'),
]

const positions = () => [
  { id: 's1', kind: 'BoardSeat', title: 'Chairperson', sortOrder: 1, isActive: true, holder: { id: 'p1', firstName: 'Alex', lastName: 'Reyes', level: 'Board' } },
  { id: 's2', kind: 'BoardSeat', title: 'Corporate Secretary', sortOrder: 2, isActive: true, holder: null },
  { id: 'g1', kind: 'GoonPosition', title: 'Head of Logistics', sortOrder: 1, isActive: true, holder: null },
  { id: 'g2', kind: 'GoonPosition', title: 'Head of Programs', sortOrder: 2, isActive: true, holder: { id: 'p2', firstName: 'Mika', lastName: 'Soriano', level: 'Goon' } },
]

function mockApi(state = { positions: positions() }) {
  server.use(
    http.get('/api/positions', () => HttpResponse.json(state.positions)),
    http.get('/api/people', ({ request }) => {
      const level = new URL(request.url).searchParams.get('level')
      const items = roster.filter((p) => !level || p.level === level)
      return HttpResponse.json({ items, total: items.length, page: 1, pageSize: 100 })
    }),
    http.get('/api/finance-roles', () =>
      HttpResponse.json({ cfo: { id: 'p1', firstName: 'Alex', lastName: 'Reyes', level: 'Board' }, assistant: null }),
    ),
  )
  return state
}

describe('PositionsPage', () => {
  it('shows the Board of Trustees and Goon positions cards with holders and vacancies', async () => {
    signInAs(boardMe)
    mockApi()
    renderWithProviders(<PositionsPage />, '/people/positions')
    const board = await screen.findByRole('table', { name: 'Board of Trustees' })
    expect(within(board).getByText('Alex Reyes')).toBeInTheDocument()
    const secretary = within(board).getByText('Corporate Secretary').closest('tr') as HTMLElement
    expect(within(secretary).getByText('Vacant')).toBeInTheDocument()
    const goons = screen.getByRole('table', { name: 'Goon positions' })
    expect(within(goons).getByRole('button', { name: 'Vacate Head of Programs' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Positions' })).toHaveAttribute('aria-current', 'page')
    expect(await screen.findByText('Non-stock corporation · 2 seats')).toBeInTheDocument()
    expect(await screen.findByText('Organizer roles · 2 goons')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'People · 4' })).toBeInTheDocument()
  })

  it('assigns a vacant position with a picker filtered to the right level', async () => {
    signInAs(boardMe)
    const state = mockApi()
    let assigned: unknown
    server.use(
      http.post('/api/positions/:id/assign', async ({ params, request }) => {
        assigned = { id: params.id, ...((await request.json()) as object) }
        state.positions = state.positions.map((p) =>
          p.id === params.id ? { ...p, holder: { id: 'p4', firstName: 'Jon', lastName: 'Tan', level: 'Goon' } } : p,
        )
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<PositionsPage />)
    await user.click(await screen.findByRole('button', { name: 'Assign Head of Logistics' }))
    const dialog = await screen.findByRole('dialog', { name: 'Assign Head of Logistics' })
    const picker = await within(dialog).findByLabelText('Goon')
    const options = within(picker).getAllByRole('option').map((o) => o.textContent)
    expect(options).toEqual(['Choose a person', 'Mika Soriano', 'Jon Tan'])
    await user.click(within(dialog).getByRole('button', { name: 'Assign' }))
    expect(within(dialog).getByText('Choose a goon.')).toBeInTheDocument()
    await user.selectOptions(picker, 'p4')
    await user.click(within(dialog).getByRole('button', { name: 'Assign' }))
    expect(await screen.findByText('Jon Tan is now Head of Logistics.')).toBeInTheDocument()
    expect(assigned).toEqual({ id: 'g1', personId: 'p4' })
    const goons = screen.getByRole('table', { name: 'Goon positions' })
    await waitFor(() => expect(within(goons).getByText('Jon Tan')).toBeInTheDocument())
  })

  it('marks people who already hold a board seat', async () => {
    signInAs(boardMe)
    mockApi()
    const user = userEvent.setup()
    renderWithProviders(<PositionsPage />)
    await user.click(await screen.findByRole('button', { name: 'Assign Corporate Secretary' }))
    const dialog = await screen.findByRole('dialog', { name: 'Assign Corporate Secretary' })
    const alex = await within(dialog).findByRole('option', { name: 'Alex Reyes (holds Chairperson)' })
    expect(alex).toBeDisabled()
  })

  it('shows wrong_level from the server', async () => {
    signInAs(boardMe)
    mockApi()
    server.use(http.post('/api/positions/:id/assign', () => problem(400, 'Wrong level', 'wrong_level')))
    const user = userEvent.setup()
    renderWithProviders(<PositionsPage />)
    await user.click(await screen.findByRole('button', { name: 'Assign Head of Logistics' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(await within(dialog).findByLabelText('Goon'), 'p4')
    await user.click(within(dialog).getByRole('button', { name: 'Assign' }))
    expect(await within(dialog).findByText("This person's level doesn't fit that role.")).toBeInTheDocument()
  })

  it('vacates with an optional reason', async () => {
    signInAs(boardMe)
    mockApi()
    let body: unknown
    server.use(
      http.post('/api/positions/:id/vacate', async ({ request }) => {
        body = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<PositionsPage />)
    await user.click(await screen.findByRole('button', { name: 'Vacate Head of Programs' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Vacate Head of Programs?' })
    await user.type(within(dialog).getByLabelText(/Reason/), 'Moved abroad')
    await user.click(within(dialog).getByRole('button', { name: 'Vacate' }))
    expect(await screen.findByText('Head of Programs is now vacant.')).toBeInTheDocument()
    expect(body).toEqual({ reason: 'Moved abroad' })
  })

  it('adds a position', async () => {
    signInAs(boardMe)
    mockApi()
    let body: unknown
    server.use(
      http.post('/api/positions', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ id: 'g3', kind: 'GoonPosition', title: 'Head of Marketing', sortOrder: 3, isActive: true, holder: null }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<PositionsPage />)
    await user.click(await screen.findByRole('button', { name: 'Add a goon position' }))
    const dialog = await screen.findByRole('dialog', { name: 'New position' })
    await user.click(within(dialog).getByRole('button', { name: 'Add position' }))
    expect(within(dialog).getByText('Enter a title.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Title'), 'Head of Marketing')
    await user.click(within(dialog).getByRole('button', { name: 'Add position' }))
    expect(await screen.findByText('Head of Marketing added.')).toBeInTheDocument()
    expect(body).toEqual({ kind: 'GoonPosition', title: 'Head of Marketing' })
  })

  it('saves finance roles (board)', async () => {
    signInAs(boardMe)
    mockApi()
    let body: unknown
    server.use(
      http.put('/api/finance-roles', async ({ request }) => {
        body = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<PositionsPage />)
    const cfo = await screen.findByLabelText('CFO')
    expect(cfo).toHaveValue('p1')
    await user.selectOptions(screen.getByLabelText('Finance assistant'), 'p2')
    await user.click(screen.getByRole('button', { name: 'Save finance roles' }))
    expect(await screen.findByText('Finance roles saved.')).toBeInTheDocument()
    expect(body).toEqual({ cfoPersonId: 'p1', assistantPersonId: 'p2' })
  })

  it('requires a CFO and does not offer self-appointment', async () => {
    signInAs({ ...boardMe, personId: 'p3' })
    mockApi()
    renderWithProviders(<PositionsPage />)
    const cfo = await screen.findByLabelText('CFO')
    expect(within(cfo).queryByRole('option', { name: 'No CFO' })).not.toBeInTheDocument()
    expect(within(cfo).getByRole('option', { name: 'Bea Lim' })).toBeDisabled()
    expect(within(cfo).getByRole('option', { name: 'Alex Reyes' })).toBeEnabled()
  })

  it('hides Assign, Vacate, + Position and Finance roles without People.Manage', async () => {
    signInAs(goonMe)
    mockApi()
    renderWithProviders(<PositionsPage />)
    const board = await screen.findByRole('table', { name: 'Board of Trustees' })
    expect(within(board).getByText('Alex Reyes')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Assign/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Vacate/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Add a (board seat|goon position)$/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Create account' })).not.toBeInTheDocument()
    expect(screen.queryByText('Finance roles')).not.toBeInTheDocument()
  })

  it('shows an error state with retry', async () => {
    signInAs(boardMe)
    mockApi()
    server.use(http.get('/api/positions', () => problem(500, 'Server error')), http.get('/api/finance-roles', () => problem(500, 'x')))
    renderWithProviders(<PositionsPage />)
    expect(await screen.findByText(/Could not load positions/)).toBeInTheDocument()
  })
})
