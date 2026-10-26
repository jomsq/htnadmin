import { useState } from 'react'
import { useParams } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { PublicHeader, PublicLayout } from '@/app/layouts/PublicLayout'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { Skeleton } from '@/components/ui/Spinner'
import { CalendarIcon } from '@/components/ui/icons'
import { formatEventDateTime } from '@/features/events/format'
import { registrationKeys, usePublicEventQuery } from '../api'
import type { RegistrationResult } from '../schemas'
import { PublicRegistrationForm } from './PublicRegistrationForm'
import { RegistrationSuccess } from './RegistrationSuccess'

/** Public registration page /r/:slug (canvas frame 10). Anonymous; mobile first. */
export function PublicRegistrationPage() {
  const { slug = '' } = useParams()
  const query = usePublicEventQuery(slug)
  const queryClient = useQueryClient()
  const [result, setResult] = useState<RegistrationResult | null>(null)
  // A new key gives "Register someone else" a fresh form (and a fresh idempotency key).
  const [formKey, setFormKey] = useState(0)

  if (query.isPending) {
    return (
      <PublicLayout header={<div className="h-28 bg-brand-700" />}>
        <div role="status" aria-label="Loading registration" className="flex flex-col gap-3 p-5">
          <Skeleton className="h-6 w-32" />
          <Skeleton className="h-16 w-full" />
          <Skeleton className="h-16 w-full" />
          <Skeleton className="h-12 w-full" />
        </div>
      </PublicLayout>
    )
  }

  if (query.isError) {
    return (
      <PublicLayout header={<PublicHeader title="Event registration" />}>
        {isApiError(query.error, 404) ? (
          <EmptyState
            icon={<CalendarIcon size={22} />}
            title="Registration isn't open"
            description="This registration page is closed or the link is wrong. Check the link from the event announcement."
          />
        ) : (
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
              Could not load the registration page. {errorMessage(query.error)}
            </Alert>
          </div>
        )}
      </PublicLayout>
    )
  }

  const event = query.data
  return (
    <PublicLayout
      brandColor={event.brandColor}
      header={<PublicHeader title={event.name} logoUrl={event.logoUrl} subtitle={`${formatEventDateTime(event.startsAt)} · ${event.venue}`} />}
    >
      {result ? (
        <RegistrationSuccess
          result={result}
          onAgain={() => {
            setResult(null)
            setFormKey((k) => k + 1)
            // Remaining seats changed with this registration.
            void queryClient.invalidateQueries({ queryKey: registrationKeys.publicEvent(slug) })
          }}
        />
      ) : (
        <PublicRegistrationForm key={formKey} slug={slug} event={event} onDone={setResult} />
      )}
    </PublicLayout>
  )
}
