import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { useAuth } from '@/app/auth/auth-context'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { ChevronLeftIcon, SearchIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import type { EventDetail } from '@/features/events/schemas'
import { useCheckInStatsQuery, useRecentScansQuery } from './api'
import { CheckInStats } from './components/CheckInStats'
import { KioskEventGate } from './components/KioskEventGate'
import { RecentScans } from './components/RecentScans'
import { ResultCard } from './components/ResultCard'
import { SearchDialog } from './components/SearchDialog'
import { StationSettings } from './components/StationSettings'
import { describeOutcome } from './format'
import { QrCameraScanner } from './scanner/QrCameraScanner'
import { readStation, useSoundSetting } from './station'
import { useFullscreen } from './useFullscreen'
import { useScanFlow } from './useScanFlow'

type InputMode = 'handheld' | 'camera'

const inputOptions = [
  { value: 'handheld', label: 'Handheld scanner' },
  { value: 'camera', label: 'Webcam' },
] as const

/** Canvas frame 12b: laptop check-in station with a handheld (keyboard) scanner or webcam. */
export function DeskPage() {
  const { eventId = '' } = useParams()
  return (
    <div className="flex min-h-dvh flex-col bg-gray-50 text-gray-900 dark:bg-gray-950 dark:text-gray-50">
      <KioskEventGate eventId={eventId}>{(event) => <Desk event={event} />}</KioskEventGate>
    </div>
  )
}

function Desk({ event }: { event: EventDetail }) {
  const { me } = useAuth()
  const { toast } = useToast()
  const [station] = useState(readStation)
  const [mode, setMode] = useState<InputMode>('handheld')
  const [sound, setSound] = useSoundSetting()
  const fullscreen = useFullscreen()
  const stats = useCheckInStatsQuery(event.id, station)
  const recent = useRecentScansQuery(event.id, station)
  const flow = useScanFlow(event.id, station, sound)
  const [searchOpen, setSearchOpen] = useState(false)
  const [value, setValue] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)
  const searchOpenRef = useRef(false)
  const base = `/events/${encodeURIComponent(event.id)}/check-in`

  useEffect(() => {
    searchOpenRef.current = searchOpen
  }, [searchOpen])

  // The handheld field keeps focus so staff never need to click; dialogs take precedence.
  const refocus = useCallback(() => {
    if (!searchOpenRef.current) inputRef.current?.focus()
  }, [])

  useEffect(() => {
    if (mode === 'handheld') refocus()
  }, [mode, refocus, flow.outcomeKey])

  function onBlur() {
    // Only take focus back when it went nowhere (a click on the page background).
    // Tabbing to another control (settings, links) is left alone so the page stays keyboard friendly.
    window.setTimeout(() => {
      const active = document.activeElement
      if (!active || active === document.body) refocus()
    }, 0)
  }

  function onSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    flow.submitPayload(value)
    setValue('')
    refocus()
  }

  function closeSearch() {
    setSearchOpen(false)
    searchOpenRef.current = false
    window.setTimeout(refocus, 0)
  }

  async function onFullscreen(on: boolean) {
    const ok = await fullscreen.toggle(on)
    if (!ok) toast({ tone: 'error', title: 'Could not switch full-screen mode.', description: 'Your browser blocked it. Try the browser menu instead.' })
  }

  const tone = flow.outcome ? describeOutcome(flow.outcome, 'desk').tone : 'gray'

  return (
    <>
      <header className="flex flex-wrap items-center gap-3 border-b border-gray-200 bg-white px-4 py-3 sm:px-6 dark:border-white/10 dark:bg-gray-900">
        <Link
          to={base}
          aria-label="Back to check-in"
          className="flex size-11 flex-none items-center justify-center rounded-lg text-gray-700 ring-1 ring-gray-300 ring-inset hover:bg-gray-50 dark:text-gray-200 dark:ring-white/15 dark:hover:bg-gray-800"
        >
          <ChevronLeftIcon size={20} />
        </Link>
        <div className="flex min-w-0 flex-col">
          <h1 className="text-base font-semibold">Check-in station · {event.name}</h1>
          <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
            {station}
            {me ? ` · ${me.firstName} ${me.lastName}` : ''}
          </span>
        </div>
        <div className="flex flex-wrap items-center gap-3 sm:ml-auto">
          <SegmentedControl options={inputOptions} value={mode} onChange={setMode} size="lg" aria-label="Scan input" />
          <Link
            to={`${base}/scan`}
            className="inline-flex min-h-11 items-center text-[0.8125rem] font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300"
          >
            Use phone instead
          </Link>
        </div>
      </header>

      <main className="flex flex-1 flex-wrap items-start gap-5 p-4 sm:p-6">
        <section aria-label="Scan" className="flex min-w-0 flex-[999_1_560px] flex-col gap-4">
          {mode === 'handheld' ? (
            <Card className="flex flex-col gap-2.5 p-5">
              <form onSubmit={onSubmit} className="flex flex-col gap-2.5">
                <label htmlFor="scan-input" className="text-sm font-medium text-gray-700 dark:text-gray-200">
                  Scan a ticket, or type a participant ID
                </label>
                <div className="flex flex-wrap gap-2.5">
                  <input
                    ref={inputRef}
                    id="scan-input"
                    type="text"
                    autoComplete="off"
                    autoCapitalize="off"
                    spellCheck={false}
                    placeholder="Waiting for scanner…"
                    aria-describedby="scan-input-hint"
                    value={value}
                    onChange={(e) => setValue(e.target.value)}
                    onBlur={onBlur}
                    className="h-14 min-w-0 flex-1 rounded-lg bg-white px-4 font-mono text-xl text-gray-900 ring-2 ring-brand-600 ring-inset placeholder:text-gray-500 focus:outline-2 focus:outline-offset-2 focus:outline-brand-600 dark:bg-gray-900 dark:text-gray-50 dark:ring-brand-300 dark:focus:outline-brand-300"
                  />
                  <Button type="submit" className="h-14 px-5 text-[0.9375rem]">
                    Check in
                  </Button>
                </div>
              </form>
              <span id="scan-input-hint" className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
                Scan the ticket QR. Typed IDs need a confirmation.
              </span>
            </Card>
          ) : (
            <QrCameraScanner
              className="h-65 rounded-lg bg-gray-900"
              tone={tone}
              caption="Laptop camera · hold the QR code up to the screen"
              fallback="Switch to Handheld scanner, or search by name or reference."
              onDecode={flow.submitPayload}
            />
          )}

          <ResultCard
            outcome={flow.outcome}
            outcomeKey={flow.outcomeKey}
            size="desk"
            onConfirm={(id) => void flow.submitManual(id)}
            confirming={flow.manualPending}
          />

          <div>
            <Button variant="secondary" size="lg" onClick={() => setSearchOpen(true)}>
              <SearchIcon size={18} />
              Search by name or reference
            </Button>
          </div>
        </section>

        <aside aria-label="Station" className="flex min-w-0 flex-[1_1_320px] flex-col gap-4">
          <Card className="px-5 py-4">
            <CheckInStats query={stats} stationLabel="This desk" />
          </Card>
          <RecentScans query={recent} />
          <StationSettings
            sound={sound}
            onSound={setSound}
            fullscreen={fullscreen.active}
            fullscreenSupported={fullscreen.supported}
            onFullscreen={(on) => void onFullscreen(on)}
          />
        </aside>
      </main>

      {searchOpen ? <SearchDialog eventId={event.id} onClose={closeSearch} onCheckIn={flow.submitManual} /> : null}
    </>
  )
}
