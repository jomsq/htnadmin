import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { boardMe, problem, server, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { CreateAccountDialog } from './components/CreateAccountDialog'
import { PersonDrawer } from './components/PersonDrawer'
import { PromoteDialog } from './components/PromoteDialog'
import type { Person } from './schemas'

const NO_CHECK_IN = 'Only people who have checked in at an event can be Members.'

const volunteer: Person = {
  id: 'p7',
  firstName: 'Paolo',
  middleName: null,
  lastName: 'Cruz',
  email: 'paolo@gmail.com',
  level: 'Apprentice',
  position: null,
  isActive: true,
  accountStatus: 'Active',
  lastSignInAt: null,
  createdAt: '2026-01-10T08:00:00Z',
  isCfo: false,
  isFinanceAssistant: false,
}

function mockDetail(extra: Record<string, unknown>) {
  server.use(http.get('/api/people/p7', () => HttpResponse.json({ person: volunteer, positions: [], history: [], ...extra })))
}

describe('Member rule UI', () => {
  it('shows the events a person attended in the drawer', async () => {
    signInAs(boardMe)
    mockDetail({
      attendance: [
        { eventId: 'e1', eventName: 'HTN Con 2026', checkedInAt: '2026-11-22T01:02:00Z' },
        { eventId: 'e2', eventName: 'HTN Meetup Baguio', checkedInAt: '2026-03-14T02:00:00Z' },
      ],
    })
    renderWithProviders(<PersonDrawer person={volunteer} onClose={vi.fn()} />)
    const drawer = await screen.findByRole('dialog', { name: 'Person details' })
    const attended = await within(drawer).findByRole('region', { name: 'Attended' })
    expect(within(attended).getByText('HTN Con 2026')).toBeInTheDocument()
    expect(within(attended).getByText('Nov 22, 2026')).toBeInTheDocument()
    expect(within(attended).getByText('HTN Meetup Baguio')).toBeInTheDocument()
  })

  it('hides "Attended" when the list is empty or absent', async () => {
    signInAs(boardMe)
    mockDetail({ attendance: [] })
    renderWithProviders(<PersonDrawer person={volunteer} onClose={vi.fn()} />)
    const drawer = await screen.findByRole('dialog', { name: 'Person details' })
    expect(await within(drawer).findByText('No changes recorded yet.')).toBeInTheDocument()
    expect(within(drawer).queryByText('Attended')).not.toBeInTheDocument()
  })

  it('shows no_check_in on the level field when moving someone to Member', async () => {
    signInAs(boardMe)
    server.use(http.post('/api/people/p7/promote', () => problem(400, 'Bad Request', 'no_check_in')))
    const user = userEvent.setup()
    renderWithProviders(<PromoteDialog person={volunteer} initialLevel="Member" onClose={vi.fn()} />)
    const dialog = await screen.findByRole('dialog', { name: 'Change level' })
    await user.type(within(dialog).getByLabelText(/Reason/), 'Never attended')
    await user.click(within(dialog).getByRole('button', { name: 'Move to Member' }))
    expect(await within(dialog).findByText(NO_CHECK_IN)).toBeInTheDocument()
    expect(within(dialog).getByRole('radiogroup', { name: 'New level' })).toHaveAccessibleDescription(NO_CHECK_IN)
  })

  it('shows no_check_in on the level field when creating a Member', async () => {
    signInAs(boardMe)
    server.use(http.post('/api/people', () => problem(400, 'Bad Request', 'no_check_in')))
    const user = userEvent.setup()
    renderWithProviders(<CreateAccountDialog open onClose={vi.fn()} />)
    const dialog = await screen.findByRole('dialog', { name: 'Create account' })
    await user.type(within(dialog).getByLabelText('First name'), 'Lia')
    await user.type(within(dialog).getByLabelText('Last name'), 'Tan')
    await user.type(within(dialog).getByLabelText('Email'), 'lia@gmail.com')
    await user.click(within(dialog).getByRole('radio', { name: 'Member' }))
    await user.click(within(dialog).getByRole('button', { name: 'Add to roster' }))
    expect(await within(dialog).findByText(NO_CHECK_IN)).toBeInTheDocument()
    expect(within(dialog).getByRole('radiogroup', { name: 'Level' })).toHaveAccessibleDescription(NO_CHECK_IN)
  })
})
