import { useId, useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
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
import { markPaidFormSchema, toMarkPaidBody, type MarkPaidValues, type RegistrationRow } from '../schemas'
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
  const schema = useMemo(() => markPaidFormSchema(needsAccountFor(accounts)), [accounts])

  const {
    register,
    control,
    handleSubmit,
    setValue,
    formState: { errors },
  } = useForm<MarkPaidValues>({
    resolver: zodResolver(schema),
    defaultValues: { method: 'GCash', paymentAccountId: defaultAccountId('GCash', accounts), reference: '', receivedAt: nowManilaInput() },
  })
  const method = useWatch({ control, name: 'method' })

  const onSubmit = handleSubmit((values) => {
    if (mutation.isPending || pending.length === 0) return
    mutation.mutate(
      { ids: pending.map((t) => t.id), ...toMarkPaidBody(values) },
      {
        onSuccess: ({ updated }) => {
          toast({ tone: 'success', title: `${updated} marked paid. Ticket emails with QR codes sent.` })
          onDone()
        },
        onError: (e) => {
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
        {accountsError ? <Alert tone="red">Could not load payment accounts. You can still record the payment.</Alert> : null}
        <MarkPaidFields
          method={method}
          onMethod={(m) => {
            setValue('method', m)
            setValue('paymentAccountId', defaultAccountId(m, accounts))
          }}
          accounts={accounts}
          fields={{ account: register('paymentAccountId'), reference: register('reference'), receivedAt: register('receivedAt') }}
          errors={{ account: errors.paymentAccountId?.message, reference: errors.reference?.message, receivedAt: errors.receivedAt?.message }}
        />
      </form>
    </Dialog>
  )
}
