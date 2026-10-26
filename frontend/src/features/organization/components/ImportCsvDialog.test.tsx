import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { boardMe, server, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { ImportCsvDialog } from './ImportCsvDialog'

const csv = () => new File(['first_name,middle_name,last_name,email,level,position\nAna,,Bautista,ana@x.ph,Member,\n'], 'people.csv', { type: 'text/csv' })

function fileInput(): HTMLInputElement {
  const input = document.querySelector<HTMLInputElement>('input[type="file"]')
  if (!input) throw new Error('no file input')
  return input
}

describe('ImportCsvDialog', () => {
  it('previews rows with per-row errors and blocks the import', async () => {
    signInAs(boardMe)
    const calls: string[] = []
    server.use(
      http.post('/api/people/import', async ({ request }) => {
        calls.push(new URL(request.url).searchParams.get('dryRun') ?? '')
        return HttpResponse.json({
          rows: [
            { row: 2, firstName: 'Ana', lastName: 'Bautista', email: 'ana@x.ph', level: 'Member', errors: [] },
            { row: 3, firstName: 'Ben', lastName: 'Cruz', email: 'alex@hackthenorth.ph', level: 'Goon', errors: ['Email is already in the organization.'] },
            { row: 4, firstName: '=cmd', lastName: 'X', email: 'x@x.ph', level: 'Member', errors: ['Cells cannot start with = + - @.'] },
          ],
          valid: 1,
          invalid: 2,
          created: 0,
        })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<ImportCsvDialog onClose={() => {}} />)
    await user.upload(fileInput(), csv())
    const table = await screen.findByRole('table', { name: 'Import preview' })
    expect(calls).toEqual(['true'])
    const row3 = within(table).getByText('Ben Cruz').closest('tr') as HTMLElement
    expect(within(row3).getByText('Email is already in the organization.')).toBeInTheDocument()
    expect(within(table).getByText('Cells cannot start with = + - @.')).toBeInTheDocument()
    expect(screen.getByText('2 with errors')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Import 1 person' })).toBeDisabled()
  })

  it('imports after a clean preview', async () => {
    signInAs(boardMe)
    const calls: string[] = []
    server.use(
      http.post('/api/people/import', async ({ request }) => {
        const dryRun = new URL(request.url).searchParams.get('dryRun') ?? ''
        calls.push(dryRun)
        const rows = [{ row: 2, firstName: 'Ana', lastName: 'Bautista', email: 'ana@x.ph', level: 'Member', errors: [] }]
        return HttpResponse.json({ rows, valid: 1, invalid: 0, created: dryRun === 'false' ? 1 : 0 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<ImportCsvDialog onClose={() => {}} />)
    await user.upload(fileInput(), csv())
    await user.click(await screen.findByRole('button', { name: 'Import 1 person' }))
    expect(await screen.findByText('Imported 1 person.')).toBeInTheDocument()
    expect(calls).toEqual(['true', 'false'])
  })

  it('rejects files that are not CSV before uploading', async () => {
    signInAs(boardMe)
    const user = userEvent.setup({ applyAccept: false })
    renderWithProviders(<ImportCsvDialog onClose={() => {}} />)
    await user.upload(fileInput(), new File(['x'], 'people.xlsx'))
    expect(await screen.findByText('Choose a .csv file.')).toBeInTheDocument()
  })
})
