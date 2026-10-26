import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useNavigate } from 'react-router'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { buttonClass } from '@/components/ui/button-styles'
import { ChevronLeftIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { useCreateEventMutation } from './api'
import { EventInfoFields } from './components/EventInfoFields'
import { eventInfoFormSchema, isEventInfoField, type EventInfoBody, type EventInfoFormInput } from './schemas'

const emptyEvent: EventInfoFormInput = { name: '', slug: '', startsAt: '', endsAt: '', venue: '', capacity: '', description: '' }

/** "/r/htn-con-2026" preview of the generated page link. */
function slugPreview(name: string, slug: string): string {
  const s = slug.trim().toLowerCase() || name.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '')
  return s ? `Attendees register at /r/${s}. Leave empty to make it from the name.` : 'Leave empty to make it from the name.'
}

/** /events/new: event info only; tickets, vouchers and registration settings follow on the Details tab. */
export function CreateEventPage() {
  const navigate = useNavigate()
  const { toast } = useToast()
  const mutation = useCreateEventMutation()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<EventInfoFormInput, unknown, EventInfoBody>({ resolver: zodResolver(eventInfoFormSchema), defaultValues: emptyEvent })
  const [name, slug] = useWatch({ control, name: ['name', 'slug'] })

  const onSubmit = handleSubmit((body) => {
    if (mutation.isPending) return
    setFormError(null)
    mutation.mutate(body, {
      onSuccess: (event) => {
        toast({ tone: 'success', title: `${event.name} was created as a draft.`, description: 'Add ticket types, then publish it.' })
        navigate(`/events/${encodeURIComponent(event.id)}/details`)
      },
      onError: (e) => {
        if (isApiError(e) && e.code === 'duplicate_slug') {
          setError('slug', { type: 'server', message: errorMessage(e) })
          return
        }
        let mapped = false
        if (isApiError(e, 400)) {
          for (const [key, message] of Object.entries(e.fieldErrors)) {
            if (isEventInfoField(key)) {
              setError(key, { type: 'server', message })
              mapped = true
            }
          }
        }
        if (!mapped) setFormError(errorMessage(e))
      },
    })
  })

  return (
    <>
      <div className="flex flex-col gap-1">
        <Link
          to="/events"
          className="inline-flex min-h-10 w-fit items-center gap-1 text-[0.8125rem] font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300"
        >
          <ChevronLeftIcon size={16} />
          Events
        </Link>
        <PageHeader title="Create event" description="New events start as drafts. Add ticket types next, then publish." />
      </div>

      <Card aria-labelledby="new-event-info" className="max-w-3xl">
        <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5 p-6">
          <h2 id="new-event-info" className="text-base font-semibold text-gray-900 dark:text-gray-50">
            Event info
          </h2>
          {formError ? (
            <Alert tone="red" live="alert">
              {formError}
            </Alert>
          ) : null}
          <EventInfoFields register={register} errors={errors} slugHint={slugPreview(name, slug)} />
          <div className="flex flex-wrap justify-end gap-2 border-t border-gray-100 pt-4 dark:border-white/10">
            <Link to="/events" className={buttonClass('secondary')}>
              Cancel
            </Link>
            <Button type="submit" loading={mutation.isPending}>
              Create event
            </Button>
          </div>
        </form>
      </Card>
    </>
  )
}
