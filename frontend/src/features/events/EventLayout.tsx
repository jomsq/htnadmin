import { useState } from 'react'
import { Link, Outlet, useParams } from 'react-router'
import { isApiError } from '@/app/api'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Dialog } from '@/components/ui/Dialog'
import { EmptyState } from '@/components/ui/EmptyState'
import { Skeleton } from '@/components/ui/Spinner'
import { TabNav } from '@/components/ui/Tabs'
import { buttonClass } from '@/components/ui/button-styles'
import { CalendarIcon, ChevronLeftIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { useCloseEventMutation, useEventQuery, usePublishEventMutation } from './api'
import { EventStatusBadge } from './components/badges'
import type { EventDetail } from './schemas'

function BackLink() {
  return (
    <Link
      to="/events"
      className="inline-flex min-h-10 w-fit items-center gap-1 text-[0.8125rem] font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300"
    >
      <ChevronLeftIcon size={16} />
      Events
    </Link>
  )
}

/** Event header + underline tabs (canvas frame 05). Only the tabs built so far are shown. */
export function EventLayout() {
  const { eventId = '' } = useParams()
  const query = useEventQuery(eventId)

  if (query.isPending) {
    return (
      <div role="status" aria-label="Loading event" className="flex flex-col gap-3">
        <BackLink />
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-11 w-full" />
      </div>
    )
  }

  if (query.isError) {
    if (isApiError(query.error, 404)) {
      return (
        <>
          <BackLink />
          <Card>
            <EmptyState
              icon={<CalendarIcon size={22} />}
              title="Event not found"
              description="It may still be a draft, or the link is wrong."
              action={
                <Link to="/events" className={buttonClass('secondary')}>
                  Back to events
                </Link>
              }
            />
          </Card>
        </>
      )
    }
    return (
      <>
        <BackLink />
        <Alert
          tone="red"
          live="alert"
          action={
            <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
              Try again
            </Button>
          }
        >
          Could not load the event. {errorMessage(query.error)}
        </Alert>
      </>
    )
  }

  const event = query.data
  const base = `/events/${encodeURIComponent(event.id)}`
  const tabs = [
    { to: `${base}/details`, label: 'Details' },
    { to: `${base}/registrations`, label: 'Registrations' },
    { to: `${base}/emails`, label: 'Emails' },
    { to: `${base}/check-in`, label: 'Check-in' },
  ]

  return (
    <>
      <header className="flex flex-col gap-3">
        <BackLink />
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="flex min-w-0 flex-wrap items-center gap-3">
            <h1 className="text-2xl font-semibold tracking-tight text-gray-900 dark:text-gray-50">{event.name}</h1>
            <EventStatusBadge status={event.status} sold={event.sold} capacity={event.capacity} />
          </div>
          <EventActions event={event} />
        </div>
        <TabNav tabs={tabs} aria-label="Event sections" />
      </header>
      <Outlet />
    </>
  )
}

function EventActions({ event }: { event: EventDetail }) {
  const canManage = useCan(Policies.EventsManage)
  const { toast } = useToast()
  const publish = usePublishEventMutation(event.id)
  const close = useCloseEventMutation(event.id)
  const [confirmClose, setConfirmClose] = useState(false)

  function onPublish() {
    if (publish.isPending) return
    publish.mutate(undefined, {
      onSuccess: () => toast({ tone: 'success', title: `${event.name} is on sale.`, description: 'The registration page is open.' }),
      onError: (e) => toast({ tone: 'error', title: 'Could not publish the event.', description: errorMessage(e) }),
    })
  }

  function onClose() {
    if (close.isPending) return
    close.mutate(undefined, {
      onSuccess: () => {
        setConfirmClose(false)
        toast({ tone: 'success', title: `Registration for ${event.name} is closed.`, description: 'You can publish it again to reopen sales.' })
      },
      onError: (e) => {
        setConfirmClose(false)
        toast({ tone: 'error', title: 'Could not close registration.', description: errorMessage(e) })
      },
    })
  }

  return (
    <div className="flex flex-wrap items-center gap-3">
      <a href={`/r/${encodeURIComponent(event.slug)}`} target="_blank" rel="noopener noreferrer" className={buttonClass('secondary')}>
        Preview registration page
        <span className="sr-only"> (opens in a new tab)</span>
      </a>
      {canManage && (event.status === 'Draft' || event.status === 'Closed') ? (
        <Button onClick={onPublish} loading={publish.isPending}>
          {event.status === 'Draft' ? 'Publish' : 'Reopen sales'}
        </Button>
      ) : null}
      {canManage && event.status === 'OnSale' ? (
        <Button variant="secondary" onClick={() => setConfirmClose(true)}>
          Close registration
        </Button>
      ) : null}
      <Dialog
        open={confirmClose}
        onClose={() => setConfirmClose(false)}
        role="alertdialog"
        size="sm"
        title="Close registration?"
        description="The registration page stops taking new sign-ups. Existing registrations are kept, and you can reopen sales later."
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfirmClose(false)}>
              Cancel
            </Button>
            <Button variant="destructive" onClick={onClose} loading={close.isPending}>
              Close registration
            </Button>
          </>
        }
      />
    </div>
  )
}
