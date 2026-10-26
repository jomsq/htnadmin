import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { detail } from '@/test/fixtures/events'
import { alreadyIn, cancelled, invalid, lookup, notPaid, recent, searchRows, stats, welcome } from '@/test/fixtures/checkin'
import { boardMe, problem, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { DeskPage } from './DeskPage'

// The desk starts in handheld mode; the webcam mode would load the camera package.
vi.mock('qr-scanner', () => ({ default: { hasCamera: () => Promise.resolve(false) } }))

const responses: Record<string, unknown> = {
  OK: welcome(),
  USED: alreadyIn(),
  UNPAID: notPaid(),
  CANCELLED: cancelled(),
  FAKE: invalid(),
  'HTN-7K3Q-9XWD': lookup('Paid'),
  'HTN-9TCW-KE6P': lookup('CheckedIn'),
  'HTN-M2PA-4RCE': lookup('PendingPayment'),
  'HTN-R4YE-QJ5M': lookup('Cancelled'),
}

function setup({ scan, statsReply }: { scan?: Parameters<typeof http.post>[1]; statsReply?: () => Response } = {}) {
  signInAs(boardMe)
  const calls = { scans: [] as Array<{ payload: string; station: string }>, stats: 0, manual: [] as unknown[], searches: [] as string[] }
  let checkedIn = 187
  server.use(
    http.get('/api/events/e1', () => HttpResponse.json(detail())),
    http.get('/api/events/e1/check-in/stats', () => {
      calls.stats++
      return statsReply ? statsReply() : HttpResponse.json(stats({ checkedIn }))
    }),
    http.get('/api/events/e1/check-in/recent', () => HttpResponse.json(recent())),
    http.post(
      '/api/events/e1/check-in/scan',
      scan ??
        (async ({ request }) => {
          const body = (await request.json()) as { payload: string; station: string }
          calls.scans.push(body)
          const res = responses[body.payload] ?? invalid()
          if (body.payload === 'OK') checkedIn++
          return HttpResponse.json(res)
        }),
    ),
    http.get('/api/events/e1/check-in/search', ({ request }) => {
      const q = new URL(request.url).searchParams.get('q') ?? ''
      calls.searches.push(q)
      return HttpResponse.json(searchRows().filter((r) => r.name.toLowerCase().includes(q.toLowerCase())))
    }),
    http.post('/api/events/e1/check-in/manual', async ({ request }) => {
      calls.manual.push(await request.json())
      return HttpResponse.json(welcome())
    }),
  )
  const utils = renderRoutes([{ path: '/events/:eventId/check-in/desk', element: <DeskPage /> }], '/events/e1/check-in/desk')
  return { ...utils, calls, user: userEvent.setup() }
}

const resultRegion = () => screen.getByRole('status', { name: 'Scan result' })

async function scanInput() {
  return screen.findByLabelText('Scan a ticket, or type a participant ID')
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('Check-in desk', () => {
  it('shows the station header, stats and recent scans', async () => {
    setup()
    expect(await screen.findByRole('heading', { level: 1, name: 'Check-in station · HTN Con 2026' })).toBeInTheDocument()
    expect(await screen.findByText('187 / 264 paid')).toBeInTheDocument()
    expect(screen.getByText('This desk: 41')).toBeInTheDocument()
    expect(screen.getByText('4 stations online')).toBeInTheDocument()
    expect(screen.getByRole('progressbar', { name: '187 of 264 paid attendees checked in' })).toBeInTheDocument()
    const recentCard = screen.getByRole('region', { name: 'Recent scans' })
    expect(await within(recentCard).findByText('Paolo Garcia')).toBeInTheDocument()
    expect(within(recentCard).getByText('Already in')).toBeInTheDocument()
    expect(within(recentCard).getByText('2 min')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Use phone instead' })).toHaveAttribute('href', '/events/e1/check-in/scan')
    expect(resultRegion()).toHaveTextContent('Ready to scan')
  })

  it.each([
    ['OK', 'Welcome in', 'Maria Santos', 'Regular ticket · HTN-7K3Q-9XWD · shirt M (for sizing) · not a member yet'],
    ['USED', 'Already checked in', 'Hannah Lee', 'Scanned 8:14 AM at Desk 1'],
    ['UNPAID', 'Not paid', 'Juan Dela Cruz', 'Send to the help desk to settle ₱1,500 (GCash or bank)'],
    ['CANCELLED', 'Cancelled', 'Rina Cruz', 'This registration was cancelled. Send them to the help desk.'],
    ['FAKE', 'Not a valid ticket', 'Unknown code', 'The QR signature does not match.'],
  ])('shows the %s result in the assertive live region', async (payload, title, name, meta) => {
    const { user } = setup()
    await user.type(await scanInput(), `${payload}{Enter}`)
    const region = resultRegion()
    expect(region).toHaveAttribute('aria-live', 'assertive')
    await waitFor(() => expect(region).toHaveTextContent(title))
    expect(region).toHaveTextContent(name)
    expect(region).toHaveTextContent(meta)
  })

  it('submits on Enter with the station name, clears the field and keeps it focused', async () => {
    window.localStorage.setItem('htn.checkin.station', 'Desk 2')
    const { user, calls } = setup()
    const input = await scanInput()
    await waitFor(() => expect(input).toHaveFocus())
    await user.type(input, 'OK{Enter}')
    await waitFor(() => expect(calls.scans).toEqual([{ payload: 'OK', station: 'Desk 2' }]))
    expect(input).toHaveValue('')
    await waitFor(() => expect(resultRegion()).toHaveTextContent('Welcome in'))
    expect(input).toHaveFocus()
    // A click on the page background gives focus straight back to the field.
    await user.click(document.body)
    await waitFor(() => expect(input).toHaveFocus())
  })

  it('drops an identical payload within 3 seconds, but sends it again afterwards', async () => {
    const realNow = Date.now.bind(Date)
    let offset = 0
    vi.spyOn(Date, 'now').mockImplementation(() => realNow() + offset)
    const { user, calls } = setup()
    const input = await scanInput()
    await user.type(input, 'USED{Enter}')
    await user.type(input, 'USED{Enter}')
    await user.type(input, 'OK{Enter}')
    await waitFor(() => expect(calls.scans.map((s) => s.payload)).toEqual(['USED', 'OK']))
    offset = 3100
    await user.type(input, 'OK{Enter}')
    await waitFor(() => expect(calls.scans.map((s) => s.payload)).toEqual(['USED', 'OK', 'OK']))
  })

  it('refreshes the stats after a scan', async () => {
    const { user, calls } = setup()
    expect(await screen.findByText('187 / 264 paid')).toBeInTheDocument()
    const before = calls.stats
    await user.type(await scanInput(), 'OK{Enter}')
    expect(await screen.findByText('188 / 264 paid')).toBeInTheDocument()
    expect(calls.stats).toBeGreaterThan(before)
  })

  it('shows "Slow down" when the server rate-limits scans', async () => {
    const { user } = setup({ scan: () => problem(429, 'Too Many Requests', 'rate_limited') })
    await user.type(await scanInput(), 'OK{Enter}')
    await waitFor(() => expect(resultRegion()).toHaveTextContent('Slow down'))
    expect(resultRegion()).toHaveTextContent('Wait a few seconds, then scan again.')
  })

  it('searches by name and checks someone in by hand', async () => {
    window.localStorage.setItem('htn.checkin.station', 'Desk 2')
    const { user, calls } = setup()
    await user.click(await screen.findByRole('button', { name: 'Search by name or reference' }))
    const dialog = await screen.findByRole('dialog', { name: 'Search by name or reference' })
    await user.type(within(dialog).getByLabelText('Name or participant ID'), 'maria')
    const results = await within(dialog).findByRole('list', { name: 'Search results' })
    expect(within(results).getByText('HTN-7K3Q-9XWD')).toBeInTheDocument()
    expect(within(results).getByText('Paid')).toBeInTheDocument()
    await user.click(within(results).getByRole('button', { name: 'Check in Maria Santos' }))
    await waitFor(() => expect(calls.manual).toEqual([{ attendeeId: 'a1', station: 'Desk 2' }]))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(resultRegion()).toHaveTextContent('Welcome in')
    await waitFor(() => expect(screen.getByLabelText('Scan a ticket, or type a participant ID')).toHaveFocus())
  })

  it('shows an empty search state', async () => {
    const { user } = setup()
    await user.click(await screen.findByRole('button', { name: 'Search by name or reference' }))
    const dialog = await screen.findByRole('dialog', { name: 'Search by name or reference' })
    await user.type(within(dialog).getByLabelText('Name or participant ID'), 'zzz')
    expect(await within(dialog).findByText('No attendees match “zzz”.')).toBeInTheDocument()
  })

  it('stores the sound setting and switches to webcam mode', async () => {
    const { user } = setup()
    const sound = await screen.findByRole('checkbox', { name: 'Sound: beep on success, buzz on error' })
    expect(sound).toBeChecked()
    await user.click(sound)
    expect(sound).not.toBeChecked()
    expect(window.localStorage.getItem('htn.checkin.sound')).toBe('off')
    // jsdom has no Fullscreen API.
    expect(screen.getByRole('checkbox', { name: /Full-screen kiosk mode/ })).toBeDisabled()
    await user.click(screen.getByRole('radio', { name: 'Webcam' }))
    expect(await screen.findByText(/No camera found on this device/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Scan a ticket, or type a participant ID')).not.toBeInTheDocument()
  })

  it('asks staff to confirm a typed participant ID; Enter in the field never confirms it', async () => {
    window.localStorage.setItem('htn.checkin.station', 'Desk 2')
    const { user, calls } = setup()
    const input = await scanInput()
    expect(input).toHaveAccessibleDescription('Scan the ticket QR. Typed IDs need a confirmation.')
    await user.type(input, 'HTN-7K3Q-9XWD{Enter}')
    const region = resultRegion()
    await waitFor(() => expect(region).toHaveTextContent('Check the ticket holder'))
    expect(region).toHaveTextContent('Maria Santos')
    expect(region).toHaveTextContent('Regular · HTN-7K3Q-9XWD · Shirt M')
    expect(region).toHaveTextContent('Paid · check their ID, then confirm the check-in')
    const confirm = screen.getByRole('button', { name: 'Check in Maria Santos' })
    // The field keeps focus, and pressing Enter there sends nothing to /manual.
    await waitFor(() => expect(input).toHaveFocus())
    await user.keyboard('{Enter}')
    expect(calls.manual).toEqual([])

    await user.click(confirm)
    await waitFor(() => expect(calls.manual).toEqual([{ attendeeId: 'a1', station: 'Desk 2' }]))
    await waitFor(() => expect(region).toHaveTextContent('Welcome in'))
    expect(screen.queryByRole('button', { name: 'Check in Maria Santos' })).not.toBeInTheDocument()
    await waitFor(() => expect(input).toHaveFocus())
  })

  it('confirms a lookup with Space while the button has focus', async () => {
    const { user, calls } = setup()
    await user.type(await scanInput(), 'HTN-7K3Q-9XWD{Enter}')
    const confirm = await screen.findByRole('button', { name: 'Check in Maria Santos' })
    confirm.focus()
    await user.keyboard(' ')
    await waitFor(() => expect(calls.manual).toHaveLength(1))
  })

  it.each([
    ['HTN-9TCW-KE6P', 'Already checked in · Scanned 8:14 AM at Desk 1'],
    ['HTN-M2PA-4RCE', 'Not paid · Send to the help desk to settle ₱1,500'],
    ['HTN-R4YE-QJ5M', 'Cancelled · This registration was cancelled.'],
  ])('shows a lookup of %s without a Check in button', async (payload, text) => {
    const { user } = setup()
    await user.type(await scanInput(), `${payload}{Enter}`)
    await waitFor(() => expect(resultRegion()).toHaveTextContent('Check the ticket holder'))
    expect(resultRegion()).toHaveTextContent(text)
    expect(screen.queryByRole('button', { name: /^Check in .+/ })).not.toBeInTheDocument()
  })

  it('explains the check-in window when the API says check-in is closed', async () => {
    const { user } = setup({
      statsReply: () => problem(409, 'Conflict', 'check_in_closed'),
      scan: () => problem(409, 'Conflict', 'check_in_closed'),
    })
    const message = 'Check-in is open from a day before the event until a day after it ends.'
    expect(await screen.findByText(message)).toBeInTheDocument()
    await user.type(await scanInput(), 'OK{Enter}')
    await waitFor(() => expect(resultRegion()).toHaveTextContent(message))
  })

  it('only searches names of 3+ characters and participant IDs of 6+', async () => {
    const { user, calls } = setup()
    await user.click(await screen.findByRole('button', { name: 'Search by name or reference' }))
    const dialog = await screen.findByRole('dialog', { name: 'Search by name or reference' })
    const field = within(dialog).getByLabelText('Name or participant ID')
    expect(field).toHaveAccessibleDescription(
      'Type at least 3 letters of a name, or at least 6 characters of a participant ID (e.g. HTN-7K).',
    )
    await user.type(field, 'ma')
    await user.clear(field)
    await user.type(field, 'HTN-7')
    // Longer than the debounce: nothing was sent for the short terms.
    await new Promise((r) => setTimeout(r, 450))
    expect(calls.searches).toEqual([])
    await user.type(field, 'K')
    await waitFor(() => expect(calls.searches).toEqual(['HTN-7K']))
    await user.clear(field)
    await user.type(field, 'mar')
    await waitFor(() => expect(calls.searches).toContain('mar'))
  })
})
