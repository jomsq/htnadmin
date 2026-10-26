import { useId, useMemo, useState } from 'react'
import { useFieldArray, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage, messageForCode } from '@/app/errors'
import { isApiError } from '@/app/api'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Checkbox, Input, Select } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import type { EventDetail } from '@/features/events/schemas'
import { usePaymentAccountsQuery } from '@/features/settings/api'
import { formatPeso } from '@/lib/format'
import { useManualRegistrationMutation, usePublicEventQuery } from '../api'
import { defaultAccountId, manualTickets, needsAccountFor } from '../helpers'
import { nowManilaInput } from '../format'
import {
  defaultFormOptions,
  emptyRegistrationValues,
  registrationFormSchema,
  resizeMembers,
  toAttendees,
  markPaidReasonErrors,
  toMarkPaidBody,
  voucherOrUndefined,
  type FormOptions,
  type RegistrationFormValues,
} from '../schemas'
import { BuyerFields, MemberCard } from './AttendeeFields'
import { MarkPaidFields } from './MarkPaidFields'
import { applyRegistrationFieldErrors } from './serverErrors'

/** "+ Manual registration" (same form as the public page; hidden tickets allowed; optional mark-paid). */
export function ManualRegistrationDialog({ event, onClose }: { event: EventDetail; onClose: () => void }) {
  // Form options come from the public page when it is open; otherwise the canvas defaults.
  const publicQuery = usePublicEventQuery(event.slug, { enabled: event.status === 'OnSale' && event.publicPage })
  const options: FormOptions = publicQuery.data
    ? { genders: publicQuery.data.genders, hearAbout: publicQuery.data.hearAbout, shirtSizes: publicQuery.data.shirtSizes }
    : defaultFormOptions
  return <ManualRegistrationForm event={event} options={options} onClose={onClose} />
}

function ManualRegistrationForm({ event, options, onClose }: { event: EventDetail; options: FormOptions; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const mutation = useManualRegistrationMutation(event.id)
  const accountsQuery = usePaymentAccountsQuery(false)
  const accounts = useMemo(() => accountsQuery.data ?? [], [accountsQuery.data])
  const tickets = manualTickets(event)
  const firstOpen = tickets.find((t) => t.sold < t.quantity) ?? tickets[0]
  const [formError, setFormError] = useState<string | null>(null)
  const [overrideNeeded, setOverrideNeeded] = useState(false)

  const schema = useMemo(
    () => registrationFormSchema({ ...options, mode: 'manual', needsAccount: needsAccountFor(accounts) }),
    [options, accounts],
  )
  const {
    register,
    control,
    handleSubmit,
    setValue,
    getValues,
    setError,
    formState: { errors },
  } = useForm<RegistrationFormValues>({
    resolver: zodResolver(schema),
    defaultValues: emptyRegistrationValues(firstOpen?.id ?? '', firstOpen?.groupSize ?? 1),
  })
  const members = useFieldArray({ control, name: 'members' })
  const ticketTypeId = useWatch({ control, name: 'ticketTypeId' })
  const markPaid = useWatch({ control, name: 'markPaid' })
  const method = useWatch({ control, name: 'payment.method' })
  const ticket = tickets.find((t) => t.id === ticketTypeId)
  const groupSize = ticket?.groupSize ?? 1

  const ticketField = register('ticketTypeId', {
    onChange: (e: { target: { value: string } }) => {
      const next = tickets.find((t) => t.id === e.target.value)
      members.replace(resizeMembers(getValues('members'), next?.groupSize ?? 1))
      if (next?.price === 0) setValue('payment.method', 'Comp')
    },
  })

  const markPaidField = register('markPaid', {
    onChange: (e: { target: { checked: boolean } }) => {
      if (!e.target.checked || getValues('payment.receivedAt')) return
      const m = ticket?.price === 0 ? 'Comp' : getValues('payment.method')
      setValue('payment.method', m)
      setValue('payment.paymentAccountId', defaultAccountId(m, accounts))
      setValue('payment.receivedAt', nowManilaInput())
    },
  })

  const onSubmit = handleSubmit((values) => {
    if (mutation.isPending) return
    if (values.markPaid) {
      const picked = tickets.find((t) => t.id === values.ticketTypeId)
      const reasonErrors = markPaidReasonErrors(values.payment, { costsSomething: (picked?.price ?? 0) > 0, overrideNeeded })
      if (reasonErrors.reason) setError('payment.reason', { type: 'required', message: reasonErrors.reason })
      if (reasonErrors.overrideCapacityReason) setError('payment.overrideCapacityReason', { type: 'required', message: reasonErrors.overrideCapacityReason })
      if (reasonErrors.reason || reasonErrors.overrideCapacityReason) return
    }
    setFormError(null)
    const markPaidBody = values.markPaid ? toMarkPaidBody(values.payment) : undefined
    if (markPaidBody && !overrideNeeded) markPaidBody.overrideCapacityReason = undefined
    mutation.mutate(
      {
        ticketTypeId: values.ticketTypeId,
        voucherCode: voucherOrUndefined(values.voucherCode),
        attendees: toAttendees(values),
        consent: true,
        markPaid: markPaidBody,
      },
      {
        onSuccess: (result) => {
          const ids = result.participantIds?.join(', ')
          toast({
            tone: 'success',
            title: `${values.buyer.firstName.trim()} ${values.buyer.lastName.trim()} is registered.`,
            description: [ids ? `Participant ID ${ids}.` : null, values.markPaid ? 'The ticket email is on its way.' : 'The billing email is on its way.']
              .filter(Boolean)
              .join(' '),
          })
          onClose()
        },
        onError: (e) => {
          if (isApiError(e, 409) && e.code === 'capacity_exceeded' && values.markPaid) {
            setOverrideNeeded(true)
            setFormError(messageForCode('capacity_exceeded') ?? null)
            return
          }
          if (applyRegistrationFieldErrors(e, setError).length) return
          if (isApiError(e) && e.code === 'invalid_voucher') {
            setError('voucherCode', { type: 'server', message: messageForCode('invalid_voucher') })
            return
          }
          setFormError(errorMessage(e))
        },
      },
    )
  })

  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="Manual registration"
      description="Same form as the public page. Hidden tickets (like sponsor comps) are available here."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={mutation.isPending}>
            {markPaid ? 'Register & send ticket' : 'Register & send billing'}
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        {formError ? (
          <Alert tone="red" live="alert">
            {formError}
          </Alert>
        ) : null}
        <Field label="Ticket" error={errors.ticketTypeId?.message}>
          {(p) => (
            <Select {...p} {...ticketField}>
              {tickets.length === 0 ? <option value="">No active tickets</option> : null}
              {tickets.map((t) => {
                const soldOut = t.sold >= t.quantity
                return (
                  <option key={t.id} value={t.id} disabled={soldOut}>
                    {t.name} · {formatPeso(t.price)}
                    {t.groupSize > 1 ? ` · group of ${t.groupSize}` : ''}
                    {t.visibility === 'Hidden' ? ' · hidden' : ''}
                    {soldOut ? ' · sold out' : ''}
                  </option>
                )
              })}
            </Select>
          )}
        </Field>

        <fieldset className="flex flex-col gap-4">
          <legend className="mb-3 text-sm font-semibold text-gray-900 dark:text-gray-50">{groupSize > 1 ? 'Buyer' : 'Attendee'}</legend>
          <div className="grid gap-4 sm:grid-cols-2">
            <BuyerFields register={register} control={control} errors={errors} options={options} eventName={event.name} audience="admin" />
          </div>
        </fieldset>

        {members.fields.length > 0 ? (
          <fieldset className="flex flex-col gap-3">
            <legend className="mb-3 text-sm font-semibold text-gray-900 dark:text-gray-50">Group members</legend>
            {members.fields.map((f, i) => (
              <MemberCard key={f.id} index={i} groupSize={groupSize} register={register} errors={errors} />
            ))}
          </fieldset>
        ) : null}

        <Field label="Voucher code" optional error={errors.voucherCode?.message}>
          {(p) => <Input {...p} {...register('voucherCode')} maxLength={30} autoComplete="off" spellCheck={false} className="font-mono uppercase" />}
        </Field>

        <div className="flex flex-col gap-1.5">
          <label className="flex items-start gap-2.5 text-[0.8125rem] text-gray-700 dark:text-gray-300">
            <Checkbox {...register('consent')} aria-invalid={errors.consent ? true : undefined} className="mt-0.5" />
            The attendee agreed to the event terms and the processing of their data under the Data Privacy Act of 2012.
          </label>
          {errors.consent?.message ? <p className="text-xs font-medium text-red-700 dark:text-red-400">{errors.consent.message}</p> : null}
        </div>

        <div className="flex flex-col gap-4 border-t border-gray-100 pt-4 dark:border-white/10">
          <label className="flex items-center gap-2.5 text-sm font-medium text-gray-900 dark:text-gray-50">
            <Checkbox {...markPaidField} />
            Mark as paid now and send the ticket
          </label>
          {markPaid ? (
            <MarkPaidFields
              method={method}
              onMethod={(m) => {
                setValue('payment.method', m)
                setValue('payment.paymentAccountId', defaultAccountId(m, accounts))
              }}
              accounts={accounts}
              accountsLoading={accountsQuery.isPending}
              fields={{
                account: register('payment.paymentAccountId'),
                reference: register('payment.reference'),
                receivedAt: register('payment.receivedAt'),
                reason: register('payment.reason'),
                overrideCapacityReason: register('payment.overrideCapacityReason'),
              }}
              errors={{
                account: errors.payment?.paymentAccountId?.message,
                reference: errors.payment?.reference?.message,
                receivedAt: errors.payment?.receivedAt?.message,
                reason: errors.payment?.reason?.message,
                overrideCapacityReason: errors.payment?.overrideCapacityReason?.message,
              }}
              compReasonRequired={(ticket?.price ?? 0) > 0}
              showOverride={overrideNeeded}
            />
          ) : null}
        </div>
      </form>
    </Dialog>
  )
}
