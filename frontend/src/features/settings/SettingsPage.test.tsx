import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { boardMe, goonMe, orgSettings, problem, server, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { SettingsPage } from './SettingsPage'

const accounts = [
  {
    id: 'a1',
    kind: 'GCash',
    label: 'GCash — HTN',
    bankName: null,
    accountName: 'HackTheNorth.PH Inc.',
    accountNumber: '0917 123 4567',
    accountNumberMasked: '•••• 4567',
    isDefault: true,
    isActive: true,
  },
  {
    id: 'a2',
    kind: 'Bank',
    label: 'BPI — HTN',
    bankName: 'BPI',
    accountName: 'HackTheNorth.PH Inc.',
    accountNumber: '1234 5678 90',
    accountNumberMasked: '•••• 7890',
    isDefault: false,
    isActive: true,
  },
]

describe('SettingsPage navigation', () => {
  it('shows the section nav in canvas order for the board', async () => {
    signInAs(boardMe)
    renderWithProviders(<SettingsPage />, '/settings')
    const nav = await screen.findByRole('navigation', { name: 'Settings sections' })
    await waitFor(() =>
      expect(within(nav).getAllByRole('link').map((a) => a.textContent)).toEqual([
        'Appearance',
        'Points',
        'Payment accounts',
        'Email branding & org theme',
        'Roles & permissions',
      ]),
    )
    expect(within(nav).getByRole('link', { name: 'Roles & permissions' })).toHaveAttribute('href', '/settings/permissions')
  })

  it('hides the organization sections without Settings.Manage', async () => {
    signInAs(goonMe)
    renderWithProviders(<SettingsPage />, '/settings?section=points')
    expect(await screen.findByRole('heading', { name: 'Appearance' })).toBeInTheDocument()
    const nav = screen.getByRole('navigation', { name: 'Settings sections' })
    expect(within(nav).queryByRole('link', { name: 'Points' })).not.toBeInTheDocument()
    expect(within(nav).queryByRole('link', { name: 'Payment accounts' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Points' })).not.toBeInTheDocument()
  })
})

describe('Points', () => {
  it('steps multipliers by 0.1 within 0–2 and previews a 40-pt task', async () => {
    signInAs(boardMe)
    let saved: unknown
    server.use(
      http.put('/api/settings/points', async ({ request }) => {
        saved = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, '/settings?section=points')
    const collab = await screen.findByLabelText('Collab multiplier')
    expect(collab).toHaveValue(0.7)
    const collabField = collab.closest('div') as HTMLElement
    expect(within(collabField.parentElement as HTMLElement).getByText('28 pts')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Increase Collab multiplier' }))
    expect(collab).toHaveValue(0.8)
    expect(within(collabField.parentElement as HTMLElement).getByText('32 pts')).toBeInTheDocument()

    const lead = screen.getByLabelText('Lead multiplier')
    for (let i = 0; i < 12; i++) await user.click(screen.getByRole('button', { name: 'Increase Lead multiplier' }))
    expect(lead).toHaveValue(2)
    expect(screen.getByRole('button', { name: 'Increase Lead multiplier' })).toBeDisabled()

    await user.click(screen.getByRole('switch', { name: 'Require approval' }))
    await user.click(screen.getByRole('button', { name: 'Save points' }))
    expect(await screen.findByText('Points settings saved.')).toBeInTheDocument()
    expect(saved).toEqual({ ...orgSettings.points, leadMultiplier: 2, collabMultiplier: 0.8, requireTaskApproval: false })
  })

  it('validates typed values and preset order, and resets to defaults', async () => {
    signInAs(boardMe)
    let calls = 0
    server.use(
      http.put('/api/settings/points', () => {
        calls++
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, '/settings?section=points')
    const assistant = await screen.findByLabelText('Assistant multiplier')
    await user.clear(assistant)
    await user.type(assistant, '2.5')
    const large = screen.getByLabelText('Large')
    await user.clear(large)
    await user.type(large, '15')
    await user.click(screen.getByRole('button', { name: 'Save points' }))
    expect(await screen.findByText('Use 0 to 2.')).toBeInTheDocument()
    expect(screen.getByText('Must be more than Medium.')).toBeInTheDocument()
    expect(calls).toBe(0)

    await user.click(screen.getByRole('button', { name: 'Reset to defaults' }))
    expect(screen.getByLabelText('Assistant multiplier')).toHaveValue(0.3)
    expect(screen.getByLabelText('Large')).toHaveValue(40)
  })
})

describe('Payment accounts', () => {
  it('lists accounts with the default flag', async () => {
    signInAs(boardMe)
    server.use(http.get('/api/settings/payment-accounts', () => HttpResponse.json(accounts)))
    renderWithProviders(<SettingsPage />, '/settings?section=payments')
    const table = await screen.findByRole('table', { name: 'Payment accounts' })
    const gcash = within(table).getByText('GCash — HTN').closest('tr') as HTMLElement
    expect(within(gcash).getByText('Default')).toBeInTheDocument()
    expect(within(gcash).getByText('0917 123 4567')).toBeInTheDocument()
  })

  it('validates the form: bank name for Bank, account number unless Cash', async () => {
    signInAs(boardMe)
    let body: unknown
    server.use(
      http.get('/api/settings/payment-accounts', () => HttpResponse.json(accounts)),
      http.post('/api/settings/payment-accounts', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ ...accounts[1], id: 'a3' }, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, '/settings?section=payments')
    await user.click((await screen.findAllByRole('button', { name: '+ Add account' }))[0])
    const dialog = await screen.findByRole('dialog', { name: 'Add payment account' })
    await user.selectOptions(within(dialog).getByLabelText('Type'), 'Bank')
    await user.click(within(dialog).getByRole('button', { name: 'Add account' }))
    expect(await within(dialog).findByText('Enter a label.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter the bank name.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter the account number.')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Bank name')).toHaveAttribute('aria-invalid', 'true')

    await user.type(within(dialog).getByLabelText('Account number'), '12ab')
    await user.click(within(dialog).getByRole('button', { name: 'Add account' }))
    expect(await within(dialog).findByText('Use digits, spaces and dashes only.')).toBeInTheDocument()

    // Cash needs neither a bank nor a number.
    await user.selectOptions(within(dialog).getByLabelText('Type'), 'Cash')
    expect(within(dialog).queryByLabelText('Account number')).not.toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Label'), 'Cash on hand')
    await user.click(within(dialog).getByRole('checkbox', { name: /Default account/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Add account' }))
    expect(await screen.findByText('Payment account added.')).toBeInTheDocument()
    expect(body).toEqual({
      kind: 'Cash',
      label: 'Cash on hand',
      bankName: null,
      accountName: 'HackTheNorth.PH Inc.',
      accountNumber: null,
      isDefault: true,
    })
  })

  it('deactivates with a required reason', async () => {
    signInAs(boardMe)
    let body: unknown
    server.use(
      http.get('/api/settings/payment-accounts', () => HttpResponse.json(accounts)),
      http.post('/api/settings/payment-accounts/:id/deactivate', async ({ request }) => {
        body = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, '/settings?section=payments')
    await user.click(await screen.findByRole('button', { name: 'Deactivate BPI — HTN' }))
    const dialog = await screen.findByRole('alertdialog', { name: 'Deactivate BPI — HTN?' })
    await user.click(within(dialog).getByRole('button', { name: 'Deactivate account' }))
    expect(await within(dialog).findByText('Enter a reason.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Reason'), 'Account closed')
    await user.click(within(dialog).getByRole('button', { name: 'Deactivate account' }))
    expect(await screen.findByText('BPI — HTN was deactivated.')).toBeInTheDocument()
    expect(body).toEqual({ reason: 'Account closed' })
  })

  it('shows an empty state', async () => {
    signInAs(boardMe)
    server.use(http.get('/api/settings/payment-accounts', () => HttpResponse.json([])))
    renderWithProviders(<SettingsPage />, '/settings?section=payments')
    expect(await screen.findByText('No payment accounts yet')).toBeInTheDocument()
  })

  it('shows an error state', async () => {
    signInAs(boardMe)
    server.use(http.get('/api/settings/payment-accounts', () => problem(500, 'Boom')))
    renderWithProviders(<SettingsPage />, '/settings?section=payments')
    expect(await screen.findByText(/Could not load payment accounts/)).toBeInTheDocument()
  })
})

describe('Email branding & org theme', () => {
  it('warns about low contrast against white and blocks saving', async () => {
    signInAs(boardMe)
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, '/settings?section=branding')
    const hex = await screen.findByLabelText('Hex')
    expect(screen.getByText(/Contrast 6\.57:1 with white text/)).toBeInTheDocument()
    await user.clear(hex)
    await user.type(hex, '#F87171')
    expect(await screen.findByText(/Low contrast: 2\.77:1 with white text/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save branding' })).toBeDisabled()
  })

  it('saves a preset swatch and applies it app-wide', async () => {
    signInAs(boardMe)
    let body: unknown
    let current = orgSettings
    server.use(
      http.get('/api/settings/org', () => HttpResponse.json(current)),
      http.put('/api/settings/branding', async ({ request }) => {
        body = await request.json()
        current = { ...current, branding: { ...current.branding, brandColor: '#1D4ED8' } }
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, '/settings?section=branding')
    await user.click(await screen.findByRole('radio', { name: 'Blue #1D4ED8' }))
    expect(screen.getByLabelText('Hex')).toHaveValue('#1D4ED8')
    await user.click(screen.getByRole('button', { name: 'Save branding' }))
    expect(await screen.findByText('Branding saved.')).toBeInTheDocument()
    expect(body).toEqual({ brandColor: '#1D4ED8', paymentDeadlineHours: 48 })
  })

  it('shows the server low_contrast error on the field', async () => {
    signInAs(boardMe)
    server.use(
      http.put('/api/settings/branding', () =>
        problem(400, 'Validation failed', 'low_contrast', { errors: { BrandColor: ['Too light for white text.'] } }),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, '/settings?section=branding')
    await user.click(await screen.findByRole('radio', { name: 'Teal #0F766E' }))
    await user.click(screen.getByRole('button', { name: 'Save branding' }))
    expect(await screen.findByText('Too light for white text.')).toBeInTheDocument()
  })

  it('uploads a PNG logo and rejects other files', async () => {
    signInAs(boardMe)
    let variant = ''
    let hasFile = false
    server.use(
      http.post('/api/settings/branding/logo', async ({ request }) => {
        variant = new URL(request.url).searchParams.get('variant') ?? ''
        const file = (await request.formData()).get('file')
        // jsdom's File crosses into undici here, so only check that the multipart field is present.
        hasFile = file !== null && typeof file === 'object'
        return HttpResponse.json({ ...orgSettings.branding, logoDarkUrl: '/api/files/f1' })
      }),
    )
    const user = userEvent.setup({ applyAccept: false })
    renderWithProviders(<SettingsPage />, '/settings?section=branding')
    await screen.findByRole('button', { name: 'Upload dark logo' })
    const inputs = document.querySelectorAll<HTMLInputElement>('input[type="file"]')
    await user.upload(inputs[1], new File(['x'], 'logo.jpg', { type: 'image/jpeg' }))
    expect(await screen.findByText('Upload a PNG file.')).toBeInTheDocument()
    await user.upload(inputs[1], new File([new Uint8Array([137, 80, 78, 71])], 'logo-dark.png', { type: 'image/png' }))
    expect(await screen.findByText('Dark logo uploaded.')).toBeInTheDocument()
    expect(variant).toBe('dark')
    expect(hasFile).toBe(true)
    expect(await screen.findByRole('img', { name: 'Current logo for dark backgrounds' })).toHaveAttribute('src', '/api/files/f1')
  })
})
