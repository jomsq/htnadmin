import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageSpinner } from '@/components/ui/Spinner'
import { buttonClass } from '@/components/ui/button-styles'
import { CalendarIcon } from '@/components/ui/icons'
import { useEventQuery } from '@/features/events/api'
import type { EventDetail } from '@/features/events/schemas'

/** Loads the event for a kiosk screen; drafts can't be checked into (the server says the same). */
export function KioskEventGate({ eventId, children }: { eventId: string; children: (event: EventDetail) => ReactNode }) {
  const query = useEventQuery(eventId)

  if (query.isPending) return <PageSpinner label="Loading event" />

  if (query.isError) {
    return (
      <div className="mx-auto flex w-full max-w-lg flex-col gap-4 p-5">
        {isApiError(query.error, 404) ? (
          <Card>
            <EmptyState
              icon={<CalendarIcon size={22} />}
              title="Event not found"
              description="It may still be a draft, or the link is wrong."
              action={
                <Link to="/events" className={buttonClass('secondary', 'lg')}>
                  Back to events
                </Link>
              }
            />
          </Card>
        ) : (
          <Alert
            tone="red"
            live="alert"
            action={
              <Button variant="secondary" onClick={() => void query.refetch()}>
                Try again
              </Button>
            }
          >
            Could not load the event. {errorMessage(query.error)}
          </Alert>
        )}
      </div>
    )
  }

  if (query.data.status === 'Draft') {
    return (
      <div className="mx-auto flex w-full max-w-lg flex-col p-5">
        <Card>
          <EmptyState
            icon={<CalendarIcon size={22} />}
            title="Check-in is not open yet"
            description="Check-in opens once the event is published."
            action={
              <Link to={`/events/${encodeURIComponent(eventId)}/details`} className={buttonClass('secondary', 'lg')}>
                Back to the event
              </Link>
            }
          />
        </Card>
      </div>
    )
  }

  return children(query.data)
}
