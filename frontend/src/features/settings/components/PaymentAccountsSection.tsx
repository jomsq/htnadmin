import { useId, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field } from '@/components/ui/Field'
import { Checkbox, Textarea } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { WalletIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { cn } from '@/lib/cn'
import { useDeactivatePaymentAccountMutation, usePaymentAccountsQuery } from '../api'
import { reasonSchema, type PaymentAccount, type ReasonValues } from '../schemas'
import { PaymentAccountDialog } from './PaymentAccountDialog'
import { SettingsCard } from './SettingsCard'

const tileClass: Record<PaymentAccount['kind'], string> = {
  GCash: 'bg-blue-50 text-blue-700 dark:bg-blue-400/10 dark:text-blue-300',
  Bank: 'bg-brand-50 text-brand-700 dark:bg-brand-300/10 dark:text-brand-300',
  Cash: 'bg-gray-100 text-gray-700 dark:bg-white/10 dark:text-gray-300',
}

/** Short tile text: GCASH, the bank name (BPI, BDO) or CASH. */
function shortName(a: PaymentAccount): string {
  if (a.kind === 'Bank') return (a.bankName || 'Bank').toUpperCase().slice(0, 6)
  return a.kind.toUpperCase()
}

type Modal = { kind: 'edit'; account: PaymentAccount | null } | { kind: 'deactivate'; account: PaymentAccount } | null

export function PaymentAccountsSection() {
  const [showInactive, setShowInactive] = useState(false)
  const query = usePaymentAccountsQuery(showInactive)
  const [modal, setModal] = useState<Modal>(null)
  const openAdd = () => setModal({ kind: 'edit', account: null })

  return (
    <SettingsCard
      id="payments"
      title="Payment accounts"
      description="Shown in billing emails. Payments are verified by hand."
      action={
        <Button variant="secondary" size="sm" onClick={openAdd}>
          + Account
        </Button>
      }
    >
      <label className="flex min-h-10 w-fit items-center gap-2.5 text-sm text-gray-700 dark:text-gray-200">
        <Checkbox checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
        Show deactivated accounts
      </label>

      {query.isPending ? (
        <div role="status" aria-label="Loading payment accounts" className="flex flex-col gap-3">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
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
          Could not load payment accounts. {errorMessage(query.error)}
        </Alert>
      ) : query.data.length === 0 ? (
        <EmptyState
          icon={<WalletIcon size={22} />}
          title="No payment accounts yet"
          description="Billing emails need at least one account people can pay into."
          action={<Button onClick={openAdd}>Add an account</Button>}
        />
      ) : (
        <ul aria-label="Payment accounts" className="-mx-6 -mb-6">
          {query.data.map((a) => (
            <li key={a.id} className="flex flex-wrap items-center gap-3.5 border-t border-gray-100 px-6 py-3.5 dark:border-white/10">
              <span aria-hidden="true" className={cn('flex size-11 shrink-0 items-center justify-center rounded-lg text-[0.6875rem] font-bold', tileClass[a.kind])}>
                {shortName(a)}
              </span>
              <div className="flex min-w-0 flex-1 flex-col">
                <span className="text-sm font-medium text-gray-900 dark:text-gray-50">{a.label}</span>
                <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
                  {a.kind === 'Cash' ? (
                    a.accountName
                  ) : (
                    <>
                      <span className="font-mono">{a.accountNumber ?? a.accountNumberMasked ?? '—'}</span> · {a.accountName}
                    </>
                  )}
                </span>
              </div>
              {a.isDefault ? <Badge color="green">Default</Badge> : null}
              {!a.isActive ? <Badge color="gray">Deactivated</Badge> : null}
              {a.isActive ? (
                <span className="inline-flex gap-1.5">
                  <Button variant="secondary" size="sm" aria-label={`Edit ${a.label}`} onClick={() => setModal({ kind: 'edit', account: a })}>
                    Edit
                  </Button>
                  <Button variant="danger" size="sm" aria-label={`Deactivate ${a.label}`} onClick={() => setModal({ kind: 'deactivate', account: a })}>
                    Deactivate
                  </Button>
                </span>
              ) : null}
            </li>
          ))}
        </ul>
      )}

      {modal?.kind === 'edit' ? <PaymentAccountDialog account={modal.account} onClose={() => setModal(null)} /> : null}
      {modal?.kind === 'deactivate' ? <DeactivateAccountDialog account={modal.account} onClose={() => setModal(null)} /> : null}
    </SettingsCard>
  )
}

function DeactivateAccountDialog({ account, onClose }: { account: PaymentAccount; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const mutation = useDeactivatePaymentAccountMutation()
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ReasonValues>({ resolver: zodResolver(reasonSchema), defaultValues: { reason: '' } })

  const onSubmit = handleSubmit(({ reason }) => {
    if (mutation.isPending) return
    mutation.mutate(
      { id: account.id, reason },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: `${account.label} was deactivated.` })
          onClose()
        },
        onError: (e) => {
          toast({ tone: 'error', title: 'Could not deactivate the account.', description: errorMessage(e) })
          onClose()
        },
      },
    )
  })

  return (
    <Dialog
      open
      onClose={onClose}
      role="alertdialog"
      size="sm"
      title={`Deactivate ${account.label}?`}
      description="It stops appearing in billing emails. Past payments keep their record."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="destructive" loading={mutation.isPending}>
            Deactivate account
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit}>
        <Field label="Reason" error={errors.reason?.message}>
          {(p) => <Textarea {...p} {...register('reason')} rows={3} />}
        </Field>
      </form>
    </Dialog>
  )
}
