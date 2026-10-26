import { useId, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { useToast } from '@/components/ui/toast-context'
import { useSaveTicketMutation } from '../api'
import { toManilaInput } from '../format'
import { ticketFormSchema, type TicketBody, type TicketFormInput, type TicketType, type TicketVisibility } from '../schemas'

const fieldNames = ['name', 'price', 'quantity', 'groupSize', 'salesStartsAt', 'salesEndsAt', 'visibility'] as const
type FieldName = (typeof fieldNames)[number]
const isFieldName = (k: string): k is FieldName => (fieldNames as readonly string[]).includes(k)

const visibilityOptions = [
  { value: 'Public', label: 'Public' },
  { value: 'Hidden', label: 'Hidden' },
] as const satisfies ReadonlyArray<{ value: TicketVisibility; label: string }>

/** Add or edit a ticket type. Price locks once any are sold; quantity can't go below sold. */
export function TicketDialog({ eventId, ticket, onClose }: { eventId: string; ticket: TicketType | null; onClose: () => void }) {
  const formId = useId()
  const visibilityLabelId = useId()
  const visibilityHintId = useId()
  const { toast } = useToast()
  const mutation = useSaveTicketMutation(eventId)
  const [formError, setFormError] = useState<string | null>(null)
  const sold = ticket?.sold ?? 0
  const priceLocked = sold > 0
  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<TicketFormInput, unknown, TicketBody>({
    resolver: zodResolver(ticketFormSchema(sold)),
    defaultValues: {
      name: ticket?.name ?? '',
      price: ticket ? String(ticket.price) : '',
      quantity: ticket ? String(ticket.quantity) : '',
      groupSize: ticket ? String(ticket.groupSize) : '1',
      salesStartsAt: toManilaInput(ticket?.salesStartsAt),
      salesEndsAt: toManilaInput(ticket?.salesEndsAt),
      visibility: ticket?.visibility ?? 'Public',
    },
  })

  const onSubmit = handleSubmit((body) => {
    if (mutation.isPending) return
    setFormError(null)
    mutation.mutate(
      { ticketId: ticket?.id, body: priceLocked && ticket ? { ...body, price: ticket.price } : body },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: ticket ? `${body.name} saved.` : `${body.name} added.` })
          onClose()
        },
        onError: (e) => {
          if (isApiError(e) && e.code === 'price_locked') {
            setError('price', { type: 'server', message: errorMessage(e) })
            return
          }
          let mapped = false
          if (isApiError(e, 400)) {
            for (const [key, message] of Object.entries(e.fieldErrors)) {
              if (isFieldName(key)) {
                setError(key, { type: 'server', message })
                mapped = true
              }
            }
          }
          if (!mapped) setFormError(errorMessage(e))
        },
      },
    )
  })

  return (
    <Dialog
      open
      onClose={onClose}
      title={ticket ? `Edit ${ticket.name}` : 'Add ticket type'}
      description="Times are Philippine time. Leave the sales window empty to sell while the event is on sale."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={mutation.isPending}>
            {ticket ? 'Save ticket type' : 'Add ticket type'}
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        {formError ? (
          <Alert tone="red" live="alert">
            {formError}
          </Alert>
        ) : null}
        <Field label="Name" error={errors.name?.message} hint="For example Regular, Student or Team of 5.">
          {(p) => <Input {...p} {...register('name')} autoComplete="off" />}
        </Field>
        <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-3">
          <Field
            label="Price (₱)"
            error={errors.price?.message}
            hint={priceLocked ? 'Locked: tickets have been sold.' : 'Use 0 for free.'}
          >
            {(p) => <Input {...p} {...register('price')} inputMode="decimal" readOnly={priceLocked} className="tabular-nums" />}
          </Field>
          <Field label="Quantity" error={errors.quantity?.message} hint={sold > 0 ? `${sold} sold so far.` : undefined}>
            {(p) => <Input {...p} {...register('quantity')} inputMode="numeric" className="tabular-nums" />}
          </Field>
          <Field label="Group size" error={errors.groupSize?.message} hint="1 for one person.">
            {(p) => <Input {...p} {...register('groupSize')} inputMode="numeric" className="tabular-nums" />}
          </Field>
        </div>
        <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">
          <Field label="Sales start" optional error={errors.salesStartsAt?.message}>
            {(p) => <Input {...p} {...register('salesStartsAt')} type="datetime-local" />}
          </Field>
          <Field label="Sales end" optional error={errors.salesEndsAt?.message}>
            {(p) => <Input {...p} {...register('salesEndsAt')} type="datetime-local" />}
          </Field>
        </div>
        <div className="flex flex-col gap-1.5">
          <span id={visibilityLabelId} className="text-sm font-medium text-gray-700 dark:text-gray-200">
            Visibility
          </span>
          <Controller
            control={control}
            name="visibility"
            render={({ field }) => (
              <SegmentedControl
                options={visibilityOptions}
                value={field.value}
                onChange={field.onChange}
                aria-labelledby={visibilityLabelId}
                aria-describedby={visibilityHintId}
                className="w-fit"
              />
            )}
          />
          <p id={visibilityHintId} className="text-xs text-gray-600 dark:text-gray-400">
            Hidden tickets are not on the registration page. Use them for sponsor comps and manual registration.
          </p>
        </div>
      </form>
    </Dialog>
  )
}
