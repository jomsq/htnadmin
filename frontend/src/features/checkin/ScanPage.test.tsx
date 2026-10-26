import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { routes } from '@/app/router'
import { detail, draftDetail } from '@/test/fixtures/events'
import { lookup, recent, stats, welcome } from '@/test/fixtures/checkin'
import { boardMe, goonMe, problem, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'

// Stand-in for the lazily imported camera package.
const camera = vi.hoisted(() => ({
  hasCamera: true,
  startError: null as unknown,
  onDecode: null as ((r: { data: string }) => void) | null,
  destroyed: 0,
}))

vi.mock('qr-scanner', () => ({
  default: class {
    static hasCamera() {
      return Promise.resolve(camera.hasCamera)
    }
    constructor(_video: HTMLVideoElement, onDecode: (r: { data: string }) => void) {
      camera.onDecode = onDecode
    }
    start() {
      return camera.startError ? Promise.reject(camera.startError) : Promise.resolve()
    }
    destroy() {
      camera.destroyed++
    }
  },
}))

beforeEach(() => {
  camera.hasCamera = true
  camera.startError = null
  camera.onDecode = null
  camera.destroyed = 0
})

function mockApi(event: Record<string, unknown> = detail()) {
  const scans: unknown[] = []
  server.use(
    http.get(`/api/events/${String(event.id)}`, () => HttpResponse.json(event)),
    http.get('/api/events/:id/check-in/stats', () => HttpResponse.json(stats())),
    http.get('/api/events/:id/check-in/recent', () => HttpResponse.json(recent())),
    http.post('/api/events/e1/check-in/scan', async ({ request }) => {
      scans.push(await request.json())
      return HttpResponse.json(welcome())
    }),
  )
  return { scans }
}

/** The real app routes, so layouts and policy guards are exercised too. */
function renderApp(path: string) {
  return { ...renderRoutes(routes, path), user: userEvent.setup() }
}

describe('Phone scanner', () => {
  it('lets a goon open the scanner in the kiosk layout and scans from the camera once per held QR', async () => {
    signInAs(goonMe)
    const { scans } = mockApi()
    renderApp('/events/e1/check-in/scan')
    expect(await screen.findByRole('heading', { level: 1, name: 'Check-in · HTN Con 2026' })).toBeInTheDocument()
    // No sidebar on kiosk screens.
    expect(screen.queryByRole('navigation', { name: /main/i })).not.toBeInTheDocument()
    expect(screen.getByText('Main entrance · Mika Soriano')).toBeInTheDocument()
    expect(await screen.findByText('187 / 264 paid')).toBeInTheDocument()
    expect(await screen.findByText('Point the camera at the ticket QR code')).toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'Scan result' })).toHaveTextContent('Waiting for a scan…')

    await waitFor(() => expect(camera.onDecode).not.toBeNull())
    act(() => {
      camera.onDecode?.({ data: 'TOKEN-1' })
      camera.onDecode?.({ data: 'TOKEN-1' })
    })
    const region = screen.getByRole('status', { name: 'Scan result' })
    await waitFor(() => expect(region).toHaveTextContent('Welcome in'))
    expect(region).toHaveTextContent('Regular · HTN-7K3Q-9XWD · Shirt M')
    expect(scans).toEqual([{ payload: 'TOKEN-1', station: 'Main entrance' }])
    expect(screen.getByRole('link', { name: 'Use a laptop + handheld scanner instead' })).toHaveAttribute('href', '/events/e1/check-in/desk')
  })

  it('needs a tap to confirm when the QR is just a participant ID', async () => {
    signInAs(goonMe)
    mockApi()
    const manual: unknown[] = []
    server.use(
      http.post('/api/events/e1/check-in/scan', () => HttpResponse.json(lookup('Paid'))),
      http.post('/api/events/e1/check-in/manual', async ({ request }) => {
        manual.push(await request.json())
        return HttpResponse.json(welcome())
      }),
    )
    const { user } = renderApp('/events/e1/check-in/scan')
    await waitFor(() => expect(camera.onDecode).not.toBeNull())
    act(() => camera.onDecode?.({ data: 'HTN-7K3Q-9XWD' }))
    const region = screen.getByRole('status', { name: 'Scan result' })
    await waitFor(() => expect(region).toHaveTextContent('Check the ticket holder'))
    expect(manual).toEqual([])
    await user.click(screen.getByRole('button', { name: 'Check in Maria Santos' }))
    await waitFor(() => expect(region).toHaveTextContent('Welcome in'))
    expect(manual).toEqual([{ attendeeId: 'a1', station: 'Main entrance' }])
  })

  it('explains when there is no camera and keeps search as the fallback', async () => {
    camera.hasCamera = false
    signInAs(boardMe)
    mockApi()
    const { user } = renderApp('/events/e1/check-in/scan')
    expect(await screen.findByRole('alert')).toHaveTextContent('No camera found on this device. Search by name or reference below instead.')
    await user.click(screen.getByRole('button', { name: 'Search by name or reference' }))
    expect(await screen.findByRole('dialog', { name: 'Search by name or reference' })).toBeInTheDocument()
  })

  it('explains when camera permission is denied', async () => {
    camera.startError = new DOMException('Permission denied', 'NotAllowedError')
    signInAs(boardMe)
    mockApi()
    renderApp('/events/e1/check-in/scan')
    expect(await screen.findByRole('alert')).toHaveTextContent('Camera access is blocked. Allow the camera for this site in your browser settings.')
  })

  it('does not open check-in for a draft event', async () => {
    signInAs(boardMe)
    mockApi(draftDetail())
    renderApp('/events/e3/check-in/scan')
    expect(await screen.findByText('Check-in opens once the event is published.')).toBeInTheDocument()
  })

  it('shows no access without CheckIn.Scan', async () => {
    signInAs({ ...goonMe, policies: ['Events.View'] })
    mockApi()
    renderApp('/events/e1/check-in/desk')
    expect(await screen.findByText('You do not have access to this page')).toBeInTheDocument()
  })
})

describe('Check-in tab', () => {
  it('shows stats, saves the station name and links to both kiosk screens', async () => {
    signInAs(goonMe)
    mockApi()
    const { user } = renderApp('/events/e1/check-in')
    const tabs = await screen.findByRole('navigation', { name: 'Event sections' })
    expect(within(tabs).getByRole('link', { name: 'Check-in' })).toHaveAttribute('aria-current', 'page')
    expect(await screen.findByText('187 / 264 paid')).toBeInTheDocument()
    expect(screen.getByText('This station: 41')).toBeInTheDocument()
    const field = screen.getByLabelText('Station name')
    expect(field).toHaveValue('Main entrance')
    await user.clear(field)
    await user.type(field, 'Desk 2')
    expect(window.localStorage.getItem('htn.checkin.station')).toBe('Desk 2')
    expect(screen.getByRole('link', { name: /Phone scanner/ })).toHaveAttribute('href', '/events/e1/check-in/scan')
    expect(screen.getByRole('link', { name: /Desk station/ })).toHaveAttribute('href', '/events/e1/check-in/desk')
  })

  it('still works when localStorage throws', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    signInAs(goonMe)
    mockApi()
    const { user } = renderApp('/events/e1/check-in')
    const field = await screen.findByLabelText('Station name')
    expect(field).toHaveValue('Main entrance')
    await user.type(field, ' 2')
    expect(field).toHaveValue('Main entrance 2')
    vi.restoreAllMocks()
  })

  it('shows the check-in window message when check-in is closed', async () => {
    signInAs(goonMe)
    mockApi()
    server.use(http.get('/api/events/e1/check-in/stats', () => problem(409, 'Conflict', 'check_in_closed')))
    renderApp('/events/e1/check-in')
    expect(await screen.findByText('Check-in is open from a day before the event until a day after it ends.')).toBeInTheDocument()
  })

  it('hides the Check-in tab without CheckIn.Scan', async () => {
    signInAs({ ...goonMe, policies: ['Events.View'] })
    mockApi()
    renderApp('/events/e1/details')
    const tabs = await screen.findByRole('navigation', { name: 'Event sections' })
    expect(within(tabs).queryByRole('link', { name: 'Check-in' })).not.toBeInTheDocument()
  })
})
