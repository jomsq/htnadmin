import { Link, useSearchParams } from 'react-router'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, cardClass } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { FilterChip } from '@/components/ui/FilterChip'
import { PageHeader } from '@/components/ui/PageHeader'
import { ProgressBar } from '@/components/ui/ProgressBar'
import { Skeleton } from '@/components/ui/Spinner'
import { buttonClass } from '@/components/ui/button-styles'
import { CalendarIcon } from '@/components/ui/icons'
import { cn } from '@/lib/cn'
import { useEventsQuery } from './api'
import { EventStatusBadge } from './components/badges'
import { coverColor, formatEventDates, percentSold } from './format'
import { eventTabSchema, type EventSummary, type EventTab } from './schemas'

const tabs: ReadonlyArray<{ value: EventTab; label: string; managersOnly?: boolean }> = [
  { value: 'upcoming', label: 'Upcoming' },
  { value: 'drafts', label: 'Drafts', managersOnly: true },
  { value: 'past', label: 'Past' },
  { value: 'all', label: 'All' },
]

const emptyText: Record<EventTab, { title: string; description: string }> = {
  upcoming: { title: 'No upcoming events', description: 'Published events that have not ended show here.' },
  drafts: { title: 'No drafts', description: 'New events start as drafts until you publish them.' },
  past: { title: 'No past events', description: 'Events move here once they end.' },
  all: { title: 'No events yet', description: 'Create the first event to start selling tickets.' },
}

export function EventsPage() {
  const canManage = useCan(Policies.EventsManage)
  const [params, setParams] = useSearchParams()
  const visibleTabs = tabs.filter((t) => !t.managersOnly || canManage)
  const parsed = eventTabSchema.safeParse(params.get('tab'))
  const tab: EventTab = parsed.success && visibleTabs.some((t) => t.value === parsed.data) ? parsed.data : 'upcoming'
  const query = useEventsQuery(tab)

  const createLink = canManage ? (
    <Link to="/events/new" className={buttonClass('primary')}>
      + Create event
    </Link>
  ) : null

  return (
    <>
      <PageHeader title="Events" description="Each event has its own committees, task board, registrations and sponsors." actions={createLink} />

      <div role="group" aria-label="Show events" className="flex flex-wrap gap-2">
        {visibleTabs.map((t) => (
          <FilterChip
            key={t.value}
            selected={t.value === tab}
            onClick={() => setParams(t.value === 'upcoming' ? {} : { tab: t.value }, { replace: true })}
          >
            {t.label}
          </FilterChip>
        ))}
      </div>

      {query.isPending ? (
        <div role="status" aria-label="Loading events" className="grid grid-cols-[repeat(auto-fill,minmax(min(320px,100%),1fr))] gap-4">
          {Array.from({ length: 3 }, (_, i) => (
            <div key={i} className={cn(cardClass, 'flex flex-col overflow-hidden')}>
              <Skeleton className="h-28 rounded-none" />
              <div className="flex flex-col gap-3 p-4">
                <Skeleton className="h-4 w-2/3" />
                <Skeleton className="h-3 w-1/2" />
                <Skeleton className="h-1.5 w-full" />
              </div>
            </div>
          ))}
        </div>
      ) : query.isError ? (
        <Alert
          tone="red"
          live="alert"
          action={
            <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
              Try again
            </Button>
          }
        >
          Could not load events. {errorMessage(query.error)}
        </Alert>
      ) : query.data.length === 0 ? (
        <Card>
          <EmptyState
            icon={<CalendarIcon size={22} />}
            title={emptyText[tab].title}
            description={emptyText[tab].description}
            action={tab === 'past' ? null : createLink}
          />
        </Card>
      ) : (
        <ul aria-label="Events" className="grid grid-cols-[repeat(auto-fill,minmax(min(320px,100%),1fr))] gap-4">
          {query.data.map((e) => (
            <li key={e.id} className="flex">
              <EventCard event={e} />
            </li>
          ))}
        </ul>
      )}
    </>
  )
}

function EventCard({ event: e }: { event: EventSummary }) {
  const pct = percentSold(e.sold, e.capacity)
  return (
    <Link
      to={`/events/${encodeURIComponent(e.id)}/details`}
      className={cn(cardClass, 'flex w-full flex-col overflow-hidden text-gray-900 transition-shadow hover:shadow-md dark:text-gray-50')}
    >
      <div className="relative flex h-28 items-end px-4 py-3" style={{ backgroundColor: coverColor(e.id) }}>
        {e.coverUrl ? <img src={e.coverUrl} alt="" className="absolute inset-0 size-full object-cover" /> : null}
        <span className="relative">
          <EventStatusBadge status={e.status} sold={e.sold} capacity={e.capacity} solid />
        </span>
      </div>
      <div className="flex flex-col gap-3 p-4">
        <div className="flex flex-col gap-1">
          <span className="text-base font-semibold">{e.name}</span>
          <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
            {formatEventDates(e.startsAt, e.endsAt)} · {e.venue}
          </span>
        </div>
        <div className="flex flex-col gap-1.5">
          <div className="flex justify-between text-[0.8125rem]">
            <span className="text-gray-600 dark:text-gray-400">Tickets sold</span>
            <span className="font-semibold tabular-nums">
              {e.sold.toLocaleString('en-PH')} / {e.capacity.toLocaleString('en-PH')}
            </span>
          </div>
          <ProgressBar value={pct} label={`${pct}% of capacity sold`} />
        </div>
      </div>
    </Link>
  )
}
