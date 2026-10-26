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
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { WalletIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { useDeactivatePaymentAccountMutation, usePaymentAccountsQuery } from '../api'
import { reasonSchema, type PaymentAccount, type ReasonValues } from '../schemas'
import { PaymentAccountDialog } from './PaymentAccountDialog'
import { SettingsCard } from './SettingsCard'

type Modal = { kind: 'edit'; account: PaymentAccount | null } | { kind: 'deactivate'; account: PaymentAccount } | null

export function PaymentAccountsSection() {
  const [showInactive, setShowInactive] = useState(false)
  const query = usePaymentAccountsQuery(showInactive)
  const [modal, setModal] = useState<Modal>(null)
  const addButton = <Button onClick={() => setModal({ kind: 'edit', account: null })}>+ Add account</Button>

  return (
    <SettingsCard
      id="payments"
      title="Payment accounts"
      description="GCash, bank and cash accounts listed in billing emails. Accounts are never deleted; deactivate them instead."
      action={addButton}
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
          action={addButton}
        />
      ) : (
        <div className="-mx-6">
          <Table minWidth={620} aria-label="Payment accounts">
            <THead>
              <tr>
                <Th>Label</Th>
                <Th>Type</Th>
                <Th>Account name</Th>
                <Th>Number</Th>
                <Th>Status</Th>
                <Th className="text-right">
                  <span className="sr-only">Actions</span>
                </Th>
              </tr>
            </THead>
            <TBody>
              {query.data.map((a) => (
                <Tr key={a.id}>
                  <Td className="font-medium text-gray-900 dark:text-gray-50">{a.label}</Td>
                  <Td>{a.kind === 'Bank' ? `Bank · ${a.bankName ?? ''}` : a.kind}</Td>
                  <Td>{a.accountName}</Td>
                  <Td className="font-mono text-[0.8125rem]">{a.accountNumber ?? a.accountNumberMasked ?? '—'}</Td>
                  <Td>
                    <span className="flex flex-wrap gap-1.5">
                      {a.isDefault ? <Badge color="blue">Default</Badge> : null}
                      {a.isActive ? <Badge color="green">Active</Badge> : <Badge color="gray">Deactivated</Badge>}
                    </span>
                  </Td>
                  <Td className="text-right">
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
                  </Td>
                </Tr>
              ))}
            </TBody>
          </Table>
        </div>
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
