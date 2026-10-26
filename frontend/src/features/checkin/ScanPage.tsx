import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useAuth } from '@/app/auth/auth-context'
import { Button } from '@/components/ui/Button'
import { ChevronLeftIcon, SearchIcon } from '@/components/ui/icons'
import type { EventDetail } from '@/features/events/schemas'
import { useCheckInStatsQuery } from './api'
import { CheckInStats } from './components/CheckInStats'
import { KioskEventGate } from './components/KioskEventGate'
import { ResultCard } from './components/ResultCard'
import { SearchDialog } from './components/SearchDialog'
import { describeOutcome } from './format'
import { QrCameraScanner } from './scanner/QrCameraScanner'
import { readStation } from './station'
import { useScanFlow } from './useScanFlow'

/** Canvas frame 12: phone scanner. Always dark (camera-first, easy on the eyes at a door), 390px-first. */
export function ScanPage() {
  const { eventId = '' } = useParams()
  return (
    <div data-theme="dark" className="flex min-h-dvh flex-col bg-gray-950 text-white [color-scheme:dark]">
      <KioskEventGate eventId={eventId}>{(event) => <Scanner event={event} />}</KioskEventGate>
    </div>
  )
}

function Scanner({ event }: { event: EventDetail }) {
  const { me } = useAuth()
  const [station] = useState(readStation)
  const stats = useCheckInStatsQuery(event.id, station)
  const flow = useScanFlow(event.id, station)
  const [searchOpen, setSearchOpen] = useState(false)
  const base = `/events/${encodeURIComponent(event.id)}/check-in`
  const tone = flow.outcome ? describeOutcome(flow.outcome, 'phone').tone : 'gray'

  return (
    <main className="mx-auto flex w-full max-w-md flex-1 flex-col">
      <header className="flex items-center gap-3 px-5 py-4">
        <Link
          to={base}
          aria-label="Back to check-in"
          className="flex size-11 flex-none items-center justify-center rounded-lg bg-gray-800 text-white hover:bg-gray-700"
        >
          <ChevronLeftIcon size={20} />
        </Link>
        <div className="flex min-w-0 flex-1 flex-col">
          <h1 className="truncate text-base font-semibold">Check-in · {event.name}</h1>
          <span className="truncate text-[0.8125rem] text-gray-300">
            {station}
            {me ? ` · ${me.firstName} ${me.lastName}` : ''}
          </span>
        </div>
      </header>

      <CheckInStats query={stats} compact className="px-5" />

      <QrCameraScanner
        className="m-5 h-80 rounded-[20px]"
        tone={tone}
        caption="Point the camera at the ticket QR code"
        fallback="Search by name or reference below instead."
        onDecode={flow.submitPayload}
      />

      <ResultCard outcome={flow.outcome} outcomeKey={flow.outcomeKey} size="phone" className="mx-5 min-h-33" />

      <div className="mt-auto flex flex-col gap-2.5 px-5 pt-4 pb-6">
        <Button variant="secondary" block className="h-12 text-[0.9375rem]" onClick={() => setSearchOpen(true)}>
          <SearchIcon size={18} />
          Search by name or reference
        </Button>
        <Link
          to={`${base}/desk`}
          className="inline-flex min-h-11 items-center justify-center text-[0.8125rem] font-medium text-red-200 hover:text-white"
        >
          Use a laptop + handheld scanner instead
        </Link>
      </div>

      {searchOpen ? <SearchDialog eventId={event.id} onClose={() => setSearchOpen(false)} onCheckIn={flow.submitManual} /> : null}
    </main>
  )
}
