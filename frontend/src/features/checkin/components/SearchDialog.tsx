import { useRef, useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { Spinner } from '@/components/ui/Spinner'
import { RegistrationStatusBadges } from '@/features/registrations/components/badges'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { useCheckInSearchQuery } from '../api'

type Props = {
  eventId: string
  onClose: () => void
  /** Manual check-in; resolves once the result card shows the outcome. */
  onCheckIn: (attendeeId: string) => Promise<void>
}

/** Search by name or participant ID, then check in by hand (no QR). */
export function SearchDialog({ eventId, onClose, onCheckIn }: Props) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [q, setQ] = useState('')
  const term = useDebouncedValue(q.trim(), 300)
  const search = useCheckInSearchQuery(eventId, term)
  const [busyId, setBusyId] = useState<string | null>(null)

  async function checkIn(attendeeId: string) {
    if (busyId) return
    setBusyId(attendeeId)
    try {
      await onCheckIn(attendeeId)
    } finally {
      setBusyId(null)
    }
    onClose()
  }

  return (
    <Dialog
      open
      onClose={onClose}
      title="Search by name or reference"
      description="For guests without their QR code. Check their ID before checking them in."
      initialFocusRef={inputRef}
    >
      <div className="flex flex-col gap-4">
        <Field label="Name or participant ID" hint="Type at least 2 characters, e.g. Maria Santos or HTN-7K3Q-9XWD.">
          {(p) => (
            <Input
              {...p}
              ref={inputRef}
              type="search"
              controlSize="xl"
              autoComplete="off"
              spellCheck={false}
              value={q}
              onChange={(e) => setQ(e.target.value)}
            />
          )}
        </Field>
        <div aria-live="polite" className="flex flex-col gap-2">
          {term.length < 2 ? null : search.isPending ? (
            <Spinner label="Searching" className="py-4 text-gray-600 dark:text-gray-400" />
          ) : search.isError ? (
            <Alert
              tone="red"
              action={
                <Button variant="secondary" size="lg" onClick={() => void search.refetch()}>
                  Try again
                </Button>
              }
            >
              Could not search. {errorMessage(search.error)}
            </Alert>
          ) : search.data.length === 0 ? (
            <p className="py-4 text-center text-sm text-gray-600 dark:text-gray-400">No attendees match “{term}”.</p>
          ) : (
            <ul aria-label="Search results" className="flex flex-col divide-y divide-gray-100 dark:divide-white/10">
              {search.data.map((r) => (
                <li key={r.attendeeId} className="flex flex-wrap items-center gap-3 py-3">
                  <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                    <span className="text-sm font-semibold text-gray-900 dark:text-gray-50">{r.name}</span>
                    <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
                      <span className="font-mono">{r.participantId}</span> · {r.ticketName}
                    </span>
                    <RegistrationStatusBadges status={r.status} />
                  </div>
                  <Button size="lg" loading={busyId === r.attendeeId} disabled={!!busyId} onClick={() => void checkIn(r.attendeeId)} aria-label={`Check in ${r.name}`}>
                    Check in
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </Dialog>
  )
}
