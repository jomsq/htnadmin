import { useId, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Checkbox, Input, Select } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useSavePaymentAccountMutation } from '../api'
import {
  paymentAccountFormSchema,
  paymentKindSchema,
  type PaymentAccount,
  type PaymentAccountBody,
  type PaymentAccountFormInput,
} from '../schemas'

const fieldNames = ['kind', 'label', 'bankName', 'accountName', 'accountNumber', 'isDefault'] as const
type FieldName = (typeof fieldNames)[number]
const isFieldName = (k: string): k is FieldName => (fieldNames as readonly string[]).includes(k)

const kindLabels = { GCash: 'GCash', Bank: 'Bank transfer', Cash: 'Cash' } as const

/** Add or edit a payment account shown in billing emails. */
export function PaymentAccountDialog({ account, onClose }: { account: PaymentAccount | null; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const mutation = useSavePaymentAccountMutation()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<PaymentAccountFormInput, unknown, PaymentAccountBody>({
    resolver: zodResolver(paymentAccountFormSchema),
    defaultValues: {
      kind: account?.kind ?? 'GCash',
      label: account?.label ?? '',
      bankName: account?.bankName ?? '',
      accountName: account?.accountName ?? 'HackTheNorth.PH Inc.',
      accountNumber: account?.accountNumber ?? '',
      isDefault: account?.isDefault ?? false,
    },
  })
  const kind = useWatch({ control, name: 'kind' })

  const onSubmit = handleSubmit((body) => {
    if (mutation.isPending) return
    setFormError(null)
    mutation.mutate(
      { id: account?.id, body },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: account ? 'Payment account saved.' : 'Payment account added.' })
          onClose()
        },
        onError: (e) => {
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
      title={account ? 'Edit payment account' : 'Add payment account'}
      description="Shown in billing emails with the payment deadline."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={mutation.isPending}>
            {account ? 'Save account' : 'Add account'}
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
        <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">
          <Field label="Type" error={errors.kind?.message}>
            {(p) => (
              <Select {...p} {...register('kind')}>
                {paymentKindSchema.options.map((k) => (
                  <option key={k} value={k}>
                    {kindLabels[k]}
                  </option>
                ))}
              </Select>
            )}
          </Field>
          <Field label="Label" error={errors.label?.message} hint="What payers see, e.g. GCash — HTN.">
            {(p) => <Input {...p} {...register('label')} autoComplete="off" />}
          </Field>
          {kind === 'Bank' ? (
            <Field label="Bank name" error={errors.bankName?.message}>
              {(p) => <Input {...p} {...register('bankName')} placeholder="e.g. BPI" autoComplete="off" />}
            </Field>
          ) : null}
          <Field label="Account name" error={errors.accountName?.message}>
            {(p) => <Input {...p} {...register('accountName')} autoComplete="off" />}
          </Field>
          {kind !== 'Cash' ? (
            <Field label="Account number" error={errors.accountNumber?.message}>
              {(p) => <Input {...p} {...register('accountNumber')} inputMode="numeric" autoComplete="off" className="font-mono" />}
            </Field>
          ) : null}
        </div>
        <label className="flex min-h-10 items-center gap-2.5 text-sm text-gray-700 dark:text-gray-200">
          <Checkbox {...register('isDefault')} />
          Default account (listed first; replaces the current default)
        </label>
      </form>
    </Dialog>
  )
}
