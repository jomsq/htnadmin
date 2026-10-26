import { useId, useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage, messageForCode } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { useToast } from '@/components/ui/toast-context'
import { usePaymentAccountsQuery } from '@/features/settings/api'
import type { PaymentAccount } from '@/features/settings/schemas'
import { formatPeso } from '@/lib/format'
import { useMarkPaidMutation } from '../api'
import { defaultAccountId, needsAccountFor } from '../helpers'
import { nowManilaInput } from '../format'
import { markPaidFormSchema, markPaidReasonErrors, toMarkPaidBody, type MarkPaidValues, type RegistrationRow } from '../schemas'
import { MarkPaidFields } from './MarkPaidFields'

export type MarkPaidTarget = Pick<RegistrationRow, 'id' | 'status' | 'total'>

/** Bulk "Mark as paid & send ticket" (canvas frame 09). Only pending registrations are sent; others are skipped. */
export function MarkPaidDialog({
  eventId,
  targets,
  onClose,
  onDone,
}: {
  eventId: string
  targets: MarkPaidTarget[]
  onClose: () => void
  onDone: () => void
}) {
  const accountsQuery = usePaymentAccountsQuery(false)
  if (accountsQuery.isPending) {
    return (
      <Dialog open onClose={onClose} title="Mark as paid & send ticket" size="md">
        <p role="status" className="text-sm text-gray-600 dark:text-gray-400">
          Loading payment accounts…
        </p>
      </Dialog>
    )
  }
  return <MarkPaidForm eventId={eventId} targets={targets} accounts={accountsQuery.data ?? []} accountsError={accountsQuery.isError} onClose={onClose} onDone={onDone} />
}

function MarkPaidForm({
  eventId,
  targets,
  accounts,
  accountsError,
  onClose,
  onDone,
}: {
  eventId: string
  targets: MarkPaidTarget[]
  accounts: PaymentAccount[]
  accountsError: boolean
  onClose: () => void
  onDone: () => void
}) {
  const formId = useId()
  const { toast } = useToast()
  const mutation = useMarkPaidMutation(eventId)
  const pending = targets.filter((t) => t.status === 'PendingPayment')
  const skipped = targets.length - pending.length
  const total = pending.reduce((sum, t) => sum + t.total, 0)
  const costsSomething = pending.some((t) => t.total > 0)
  const schema = useMemo(() => markPaidFormSchema(needsAccountFor(accounts)), [accounts])
  const [overrideNeeded, setOverrideNeeded] = useState(false)

  const {
    register,
    control,
    handleSubmit,
    setValue,
    setError,
    formState: { errors },
  } = useForm<MarkPaidValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      method: 'GCash',
      paymentAccountId: defaultAccountId('GCash', accounts),
      reference: '',
      receivedAt: nowManilaInput(),
      reason: '',
      overrideCapacityReason: '',
    },
  })
  const method = useWatch({ control, name: 'method' })

  const onSubmit = handleSubmit((values) => {
    if (mutation.isPending || pending.length === 0) return
    const reasonErrors = markPaidReasonErrors(values, { costsSomething, overrideNeeded })
    if (reasonErrors.reason) setError('reason', { type: 'required', message: reasonErrors.reason })
    if (reasonErrors.overrideCapacityReason) setError('overrideCapacityReason', { type: 'required', message: reasonErrors.overrideCapacityReason })
    if (reasonErrors.reason || reasonErrors.overrideCapacityReason) return
    const body = toMarkPaidBody(values)
    mutation.mutate(
      { ids: pending.map((t) => t.id), ...body, overrideCapacityReason: overrideNeeded ? body.overrideCapacityReason : undefined },
      {
        onSuccess: ({ updated }) => {
          toast({ tone: 'success', title: `${updated} marked paid. Ticket emails with QR codes sent.` })
          onDone()
        },
        onError: (e) => {
          if (isApiError(e, 409) && e.code === 'capacity_exceeded') {
            setOverrideNeeded(true)
            return
          }
          toast({ tone: 'error', title: 'Could not mark as paid.', description: errorMessage(e, { 409: 'Some registrations changed in the meantime. Refresh and try again.' }) })
        },
      },
    )
  })

  const count = pending.length
  return (
    <Dialog
      open
      onClose={onClose}
      size="md"
      title="Mark as paid & send ticket"
      description={
        count > 0
          ? `${count} ${count === 1 ? 'registration' : 'registrations'} · Total ${formatPeso(total)}. Each attendee gets a ticket email with a QR code right away.`
          : 'None of the selected registrations are pending payment.'
      }
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={mutation.isPending} disabled={count === 0}>
            Mark as paid & send ticket
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        {skipped > 0 ? (
          <Alert tone="yellow">
            {skipped} selected {skipped === 1 ? 'registration is' : 'registrations are'} not pending payment and will be skipped.
          </Alert>
        ) : null}
        {overrideNeeded ? (
          <Alert tone="yellow" live="alert">
            {messageForCode('capacity_exceeded')}
          </Alert>
        ) : null}
        {accountsError ? <Alert tone="red">Could not load payment accounts. You can still record the payment.</Alert> : null}
        <MarkPaidFields
          method={method}
          onMethod={(m) => {
            setValue('method', m)
            setValue('paymentAccountId', defaultAccountId(m, accounts))
          }}
          accounts={accounts}
          fields={{
            account: register('paymentAccountId'),
            reference: register('reference'),
            receivedAt: register('receivedAt'),
            reason: register('reason'),
            overrideCapacityReason: register('overrideCapacityReason'),
          }}
          errors={{
            account: errors.paymentAccountId?.message,
            reference: errors.reference?.message,
            receivedAt: errors.receivedAt?.message,
            reason: errors.reason?.message,
            overrideCapacityReason: errors.overrideCapacityReason?.message,
          }}
          compReasonRequired={costsSomething}
          showOverride={overrideNeeded}
        />
      </form>
    </Dialog>
  )
}
