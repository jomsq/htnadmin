import { useState } from 'react'
import { useParams } from 'react-router'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { FilterChip } from '@/components/ui/FilterChip'
import { Checkbox } from '@/components/ui/Input'
import { Pagination } from '@/components/ui/Pagination'
import { SelectionBar } from '@/components/ui/SelectionBar'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { buttonClass, selectionBarButtonClass } from '@/components/ui/button-styles'
import { PeopleIcon, SearchIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { useEventQuery } from '@/features/events/api'
import { formatShortDay } from '@/features/events/format'
import { cn } from '@/lib/cn'
import { formatPeso } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { exportCsvUrl, useRegistrationsQuery, useResendBillingMutation, type RegistrationFilter } from './api'
import { RegistrationStatusBadges } from './components/badges'
import { ManualRegistrationDialog } from './components/ManualRegistrationDialog'
import { MarkPaidDialog, type MarkPaidTarget } from './components/MarkPaidDialog'
import { RegistrationDrawer } from './components/RegistrationDrawer'
import { buyerLabel, paymentCell } from './format'
import type { RegistrationCounts, RegistrationRow, RegistrationStatus } from './schemas'

const PAGE_SIZE = 25

type ChipKey = 'all' | 'pendingPayment' | 'overdue' | 'paid' | 'checkedIn' | 'cancelled'

const chips: ReadonlyArray<{ key: ChipKey; label: string; status?: RegistrationStatus; overdue?: boolean }> = [
  { key: 'all', label: 'All' },
  { key: 'pendingPayment', label: 'Pending payment', status: 'PendingPayment' },
  { key: 'overdue', label: 'Overdue', status: 'PendingPayment', overdue: true },
  { key: 'paid', label: 'Paid', status: 'Paid' },
  { key: 'checkedIn', label: 'Checked in', status: 'CheckedIn' },
  { key: 'cancelled', label: 'Cancelled', status: 'Cancelled' },
]

/** Registrations & payments (canvas frame 09). The route is gated on Registrations.Manage; the API enforces it too. */
export function RegistrationsPage() {
  const { eventId = '' } = useParams()
  const eventQuery = useEventQuery(eventId)
  const { toast } = useToast()
  const [chip, setChip] = useState<ChipKey>('all')
  const [searchInput, setSearchInput] = useState('')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<Map<string, MarkPaidTarget>>(() => new Map())
  const [open, setOpen] = useState<RegistrationRow | null>(null)
  const [markPaid, setMarkPaid] = useState<MarkPaidTarget[] | null>(null)
  const [manual, setManual] = useState(false)
  const resend = useResendBillingMutation(eventId)

  const search = useDebouncedValue(searchInput.trim(), 300)
  const [lastSearch, setLastSearch] = useState(search)
  if (search !== lastSearch) {
    // New search text starts again at page 1 with nothing selected (state adjusted during render).
    setLastSearch(search)
    setPage(1)
    setSelected(new Map())
  }

  const active = chips.find((c) => c.key === chip) ?? chips[0]
  const filter: RegistrationFilter = { status: active.status, overdue: active.overdue, search: search || undefined, page, pageSize: PAGE_SIZE }
  const query = useRegistrationsQuery(eventId, filter)
  const data = query.data
  const event = eventQuery.data
  const canManual = !!event?.manualRegistration
  const filtered = chip !== 'all' || !!search

  const rows = data?.items ?? []
  const pageSelected = rows.length > 0 && rows.every((r) => selected.has(r.id))
  const someSelected = rows.some((r) => selected.has(r.id))
  const selectedList = [...selected.values()]
  const selectedTotal = selectedList.reduce((sum, r) => sum + r.total, 0)
  const pendingSelected = selectedList.filter((r) => r.status === 'PendingPayment')

  function pickChip(key: ChipKey) {
    setChip(key)
    setPage(1)
    setSelected(new Map())
  }

  function toggle(r: RegistrationRow) {
    setSelected((prev) => {
      const next = new Map(prev)
      if (next.has(r.id)) next.delete(r.id)
      else next.set(r.id, { id: r.id, status: r.status, total: r.total })
      return next
    })
  }

  function togglePage() {
    setSelected((prev) => {
      const next = new Map(prev)
      if (pageSelected) rows.forEach((r) => next.delete(r.id))
      else rows.forEach((r) => next.set(r.id, { id: r.id, status: r.status, total: r.total }))
      return next
    })
  }

  function onResend() {
    if (resend.isPending) return
    if (pendingSelected.length === 0) {
      toast({ tone: 'info', title: 'Billing emails go to pending registrations only.', description: 'None of the selected registrations are pending payment.' })
      return
    }
    resend.mutate(
      pendingSelected.map((r) => r.id),
      {
        onSuccess: ({ sent, failed }) => {
          setSelected(new Map())
          toast({
            tone: failed ? 'info' : 'success',
            title: `Billing email resent to ${sent} ${sent === 1 ? 'registration' : 'registrations'}.`,
            description: failed ? `${failed} could not be sent. Try those again from the registration.` : undefined,
          })
        },
        onError: (e) => toast({ tone: 'error', title: 'Could not resend billing emails.', description: errorMessage(e) }),
      },
    )
  }

  const manualButton = canManual ? <Button onClick={() => setManual(true)}>+ Manual registration</Button> : null

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-gray-600 dark:text-gray-400">Payments are checked by hand. The participant ID is the payment reference.</p>
        <div className="flex flex-wrap items-center gap-3">
          <a href={exportCsvUrl(eventId, filter)} download className={buttonClass('secondary')}>
            Export CSV
          </a>
          {manualButton}
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <div role="group" aria-label="Filter by status" className="flex flex-wrap gap-2">
          {chips.map((c) => (
            <FilterChip key={c.key} selected={chip === c.key} onClick={() => pickChip(c.key)}>
              {c.label}
              {data ? <ChipCount counts={data.counts} k={c.key} /> : null}
            </FilterChip>
          ))}
        </div>
        <label className="flex h-9 w-full min-w-[240px] items-center gap-2 rounded-md bg-white px-3 text-sm text-gray-600 ring-1 ring-gray-300 ring-inset focus-within:outline-2 focus-within:outline-brand-600 sm:ml-auto sm:w-auto dark:bg-gray-900 dark:text-gray-400 dark:ring-white/15">
          <span className="sr-only">Search registrations</span>
          <SearchIcon size={16} />
          <input
            type="search"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="Name, email, ID or payment ref"
            className="min-w-0 flex-1 bg-transparent text-gray-900 outline-none placeholder:text-gray-500 dark:text-gray-50"
          />
        </label>
      </div>

      {selected.size > 0 ? (
        <SelectionBar
          summary={
            <>
              <span className="text-sm font-semibold">{selected.size} selected</span>
              <span className="text-[0.8125rem] text-gray-300">Total {formatPeso(selectedTotal)}</span>
            </>
          }
        >
          <button type="button" className={selectionBarButtonClass()} onClick={onResend} disabled={resend.isPending}>
            Resend billing email
          </button>
          <button type="button" className={selectionBarButtonClass(true)} onClick={() => setMarkPaid(selectedList)}>
            Mark as paid & send ticket
          </button>
        </SelectionBar>
      ) : null}

      <Card aria-label="Registrations">
        {query.isPending ? (
          <LoadingRows />
        ) : query.isError || !data ? (
          <div className="p-5">
            <Alert
              tone="red"
              live="alert"
              action={
                <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
                  Try again
                </Button>
              }
            >
              Could not load registrations. {errorMessage(query.error)}
            </Alert>
          </div>
        ) : rows.length === 0 ? (
          <EmptyState
            icon={<PeopleIcon size={22} />}
            title={filtered ? 'No registrations match' : 'No registrations yet'}
            description={filtered ? 'Try another name, email, participant ID or status.' : 'Registrations from the public page and manual sign-ups show here.'}
            action={filtered ? null : manualButton}
          />
        ) : (
          <>
            <Table minWidth={900} aria-busy={query.isFetching || undefined}>
              <THead>
                <tr>
                  <Th className="w-11">
                    <Checkbox
                      aria-label="Select all on this page"
                      checked={pageSelected}
                      ref={(el) => {
                        if (el) el.indeterminate = someSelected && !pageSelected
                      }}
                      onChange={togglePage}
                      className="size-[18px]"
                    />
                  </Th>
                  <Th>Attendee</Th>
                  <Th>Ticket</Th>
                  <Th>Payment</Th>
                  <Th className="text-right">Amount</Th>
                  <Th>Status</Th>
                  <Th>Registered</Th>
                </tr>
              </THead>
              <TBody>
                {rows.map((r) => {
                  const pay = paymentCell(r)
                  const isSelected = selected.has(r.id)
                  return (
                    <Tr key={r.id} className={cn(isSelected && 'bg-brand-50 dark:bg-brand-300/10', open?.id === r.id && !isSelected && 'bg-gray-50 dark:bg-white/5')}>
                      <Td>
                        <Checkbox aria-label={`Select ${buyerLabel(r)}`} checked={isSelected} onChange={() => toggle(r)} className="size-[18px]" />
                      </Td>
                      <Td>
                        <button type="button" onClick={() => setOpen(r)} className="flex min-h-10 flex-col items-start text-left">
                          <span className="font-medium text-gray-900 hover:text-brand-700 dark:text-gray-50 dark:hover:text-brand-300">{buyerLabel(r)}</span>
                          <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">{r.buyerEmail}</span>
                          <ParticipantIds ids={r.participantIds} />
                        </button>
                      </Td>
                      <Td>{r.ticketName}</Td>
                      <Td>
                        <span className="flex flex-col">
                          <span>{pay.primary}</span>
                          {pay.secondary ? <span className="text-xs text-gray-600 dark:text-gray-400">{pay.secondary}</span> : null}
                        </span>
                      </Td>
                      <Td numeric className="font-medium text-gray-900 dark:text-gray-50">
                        {formatPeso(r.total)}
                      </Td>
                      <Td>
                        <RegistrationStatusBadges status={r.status} overdue={r.overdue} />
                      </Td>
                      <Td className="whitespace-nowrap">{formatShortDay(r.createdAt)}</Td>
                    </Tr>
                  )
                })}
              </TBody>
            </Table>
            <Pagination page={data.page} pageSize={data.pageSize} total={data.total} onPage={setPage} />
          </>
        )}
      </Card>

      {open ? (
        <RegistrationDrawer
          eventId={eventId}
          row={open}
          onClose={() => setOpen(null)}
          onMarkPaid={(r) => setMarkPaid([{ id: r.id, status: r.status, total: r.total }])}
        />
      ) : null}
      {markPaid ? (
        <MarkPaidDialog
          eventId={eventId}
          targets={markPaid}
          onClose={() => setMarkPaid(null)}
          onDone={() => {
            setMarkPaid(null)
            setSelected(new Map())
          }}
        />
      ) : null}
      {manual && event ? <ManualRegistrationDialog event={event} onClose={() => setManual(false)} /> : null}
    </>
  )
}

function ChipCount({ counts, k }: { counts: RegistrationCounts; k: ChipKey }) {
  return (
    <span className="text-gray-600 tabular-nums dark:text-gray-400">
      <span className="sr-only">,</span> {counts[k]}
    </span>
  )
}

function ParticipantIds({ ids }: { ids: string[] }) {
  if (ids.length === 0) return null
  return (
    <span className="font-mono text-xs text-gray-700 dark:text-gray-300">
      {ids[0]}
      {ids.length > 1 ? <span className="font-sans text-gray-600 dark:text-gray-400"> +{ids.length - 1} more</span> : null}
    </span>
  )
}

function LoadingRows() {
  return (
    <div role="status" aria-label="Loading registrations" className="flex flex-col">
      {Array.from({ length: 6 }, (_, i) => (
        <div key={i} className="flex items-center gap-3 border-t border-gray-100 px-5 py-3 first:border-t-0 dark:border-white/5">
          <Skeleton className="size-4" />
          <div className="flex flex-1 flex-col gap-1.5">
            <Skeleton className="h-3.5 w-40" />
            <Skeleton className="h-3 w-56" />
          </div>
          <Skeleton className="h-5 w-20" />
        </div>
      ))}
    </div>
  )
}
