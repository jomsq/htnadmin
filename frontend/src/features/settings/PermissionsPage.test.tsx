import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { goonMe, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { PermissionsPage } from './PermissionsPage'
import { permissionRows } from './permissions'

describe('PermissionsPage', () => {
  it('renders the read-only matrix from permissions.ts', async () => {
    signInAs(goonMe)
    renderWithProviders(<PermissionsPage />, '/settings/permissions')
    expect(screen.getByRole('heading', { level: 1, name: 'Roles & permissions' })).toBeInTheDocument()
    const table = screen.getByRole('table', { name: 'Permissions matrix' })
    const headers = within(table).getAllByRole('columnheader').map((h) => h.textContent)
    expect(headers).toEqual(['Policy', 'Board (admin)', 'Committee lead', 'Goon', 'Apprentice'])
    expect(within(table).getAllByRole('rowheader')).toHaveLength(permissionRows.length)

    const post = within(table).getByRole('rowheader', { name: /Finance\.Post/ }).closest('tr') as HTMLElement
    expect(within(post).getByText('CFO only')).toBeInTheDocument()
    expect(within(post).getByText('finance assistant: add only, stays Pending')).toBeInTheDocument()

    const scan = within(table).getByRole('rowheader', { name: /CheckIn\.Scan/ }).closest('tr') as HTMLElement
    expect(within(scan).getAllByText('Allowed')).toHaveLength(4)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(await screen.findByRole('link', { name: 'Settings' })).toHaveAttribute('href', '/settings')
  })
})
