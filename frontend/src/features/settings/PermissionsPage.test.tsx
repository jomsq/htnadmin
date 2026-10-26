import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { goonMe, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { PermissionsPage } from './PermissionsPage'
import { permissionGroups } from './permissions'

describe('PermissionsPage', () => {
  it('renders the read-only capability matrix in canvas order', async () => {
    signInAs(goonMe)
    renderWithProviders(<PermissionsPage />, '/settings/permissions')
    expect(screen.getByRole('heading', { level: 1, name: 'Roles & permissions' })).toBeInTheDocument()
    const table = screen.getByRole('table', { name: 'Permissions matrix' })
    const headers = within(table).getAllByRole('columnheader').map((h) => h.textContent)
    expect(headers).toEqual([
      'Capability',
      'Admin · BoardFull login',
      'Committee leadGoon, per event',
      'GoonLimited login',
      'ApprenticeLimited login',
      'Member · VolunteerNo login',
    ])
    const rowCount = permissionGroups.reduce((n, g) => n + g.rows.length, 0)
    expect(within(table).getAllByRole('rowheader')).toHaveLength(rowCount + permissionGroups.length)

    const post = within(table).getByRole('rowheader', { name: 'Post money in / out, budgets, close month' }).closest('tr') as HTMLElement
    expect(within(post).getByText('CFO only')).toBeInTheDocument()
    expect(within(post).getByText('1 assistant, CFO approves')).toBeInTheDocument()

    const scan = within(table).getByRole('rowheader', { name: 'Check-in scanner' }).closest('tr') as HTMLElement
    expect(within(scan).getAllByText('Scan')).toHaveLength(3)
    expect(within(scan).getByText('Full')).toBeInTheDocument()
    expect(within(scan).getByText('None')).toBeInTheDocument()

    expect(screen.getByRole('list', { name: 'Legend' })).toHaveTextContent(/Own\s*· only their committee or their own tasks/)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(await screen.findByRole('link', { name: 'Settings' })).toHaveAttribute('href', '/settings')
  })
})
