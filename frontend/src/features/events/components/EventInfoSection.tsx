import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { useToast } from '@/components/ui/toast-context'
import { useUpdateEventMutation } from '../api'
import { toManilaInput } from '../format'
import { eventInfoFormSchema, isEventInfoField, updateBodyFrom, type EventDetail, type EventInfoBody, type EventInfoFormInput } from '../schemas'
import { CoverUpload } from './CoverUpload'
import { EventInfoFields } from './EventInfoFields'

function toForm(e: EventDetail): EventInfoFormInput {
  return {
    name: e.name,
    slug: e.slug,
    startsAt: toManilaInput(e.startsAt),
    endsAt: toManilaInput(e.endsAt),
    venue: e.venue,
    capacity: String(e.capacity),
    description: e.description ?? '',
  }
}

/** Details › Event info. Read-only (disabled fieldset, no save) without Events.Manage. */
export function EventInfoSection({ event, canManage }: { event: EventDetail; canManage: boolean }) {
  const { toast } = useToast()
  const mutation = useUpdateEventMutation(event.id)
  const [formError, setFormError] = useState<string | null>(null)
  const slugLocked = event.status !== 'Draft'
  const {
    register,
    handleSubmit,
    setError,
    reset,
    formState: { errors, isDirty },
  } = useForm<EventInfoFormInput, unknown, EventInfoBody>({ resolver: zodResolver(eventInfoFormSchema), values: toForm(event) })

  const onSubmit = handleSubmit((info) => {
    if (mutation.isPending || !canManage) return
    setFormError(null)
    const body = {
      ...updateBodyFrom(event),
      ...info,
      slug: slugLocked ? event.slug : (info.slug ?? event.slug),
      description: info.description ?? null,
    }
    mutation.mutate(body, {
      onSuccess: (saved) => {
        reset(toForm(saved))
        toast({ tone: 'success', title: 'Event info saved.' })
      },
      onError: (e) => {
        if (isApiError(e) && (e.code === 'duplicate_slug' || e.code === 'slug_locked')) {
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

  const slugHint = slugLocked
    ? `Locked after publishing. Attendees register at /r/${event.slug}.`
    : 'Lower-case letters, numbers and dashes. It locks once the event is published.'

  return (
    <Card id="info" aria-labelledby="info-heading" className="scroll-mt-6">
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5 p-6">
        <h2 id="info-heading" className="text-base font-semibold text-gray-900 dark:text-gray-50">
          Event info
        </h2>
        {formError ? (
          <Alert tone="red" live="alert">
            {formError}
          </Alert>
        ) : null}
        <fieldset disabled={!canManage} className="flex min-w-0 flex-col gap-5">
          <legend className="sr-only">Event info</legend>
          <EventInfoFields register={register} errors={errors} slugHint={slugHint} slugLocked={slugLocked} />
        </fieldset>
        <CoverUpload event={event} canManage={canManage} />
        {canManage ? (
          <div className="flex justify-end">
            <Button type="submit" loading={mutation.isPending} disabled={!isDirty}>
              Save event info
            </Button>
          </div>
        ) : null}
      </form>
    </Card>
  )
}
