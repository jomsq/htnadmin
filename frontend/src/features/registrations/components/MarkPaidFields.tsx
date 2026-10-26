import { useId } from 'react'
import type { UseFormRegisterReturn } from 'react-hook-form'
import { Field } from '@/components/ui/Field'
import { Input, Select } from '@/components/ui/Input'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import type { PaymentAccount } from '@/features/settings/schemas'
import { methodLabels } from '../format'
import { accountsFor } from '../helpers'
import { paymentMethodSchema, type PaymentMethod } from '../schemas'

const methodOptions = paymentMethodSchema.options.map((m) => ({ value: m, label: m === 'Bank' ? 'Bank transfer' : m }))

type Props = {
  method: PaymentMethod
  onMethod: (m: PaymentMethod) => void
  accounts: PaymentAccount[]
  accountsLoading?: boolean
  fields: { account: UseFormRegisterReturn; reference: UseFormRegisterReturn; receivedAt: UseFormRegisterReturn }
  errors: { account?: string; reference?: string; receivedAt?: string }
}

/** Method, payment account (from Settings), reference and received date. Shared by bulk mark-paid and manual registration. */
export function MarkPaidFields({ method, onMethod, accounts, accountsLoading, fields, errors }: Props) {
  const fitting = accountsFor(method, accounts)
  const methodId = useId()
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1.5">
        <span id={methodId} className="text-sm font-medium text-gray-700 dark:text-gray-200">
          Method
        </span>
        <SegmentedControl aria-labelledby={methodId} options={methodOptions} value={method} onChange={onMethod} />
      </div>
      {method === 'Comp' ? (
        <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">Comped seats are free (sponsor or guest). No account or reference is recorded.</p>
      ) : (
        <>
          <Field
            label="Payment account"
            error={errors.account}
            hint={!accountsLoading && fitting.length === 0 ? `No active ${methodLabels[method]} account in Settings. You can still record the payment.` : undefined}
          >
            {(p) => (
              // Remount per method: the options change, so the browser would drop the new value.
              <Select key={method} {...p} {...fields.account} disabled={fitting.length === 0}>
                <option value="">{accountsLoading ? 'Loading accounts…' : 'Select an account'}</option>
                {fitting.map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.label}
                    {a.accountNumberMasked ? ` · ${a.accountNumberMasked}` : ''}
                  </option>
                ))}
              </Select>
            )}
          </Field>
          <Field label="Reference" optional error={errors.reference} hint="The GCash or bank reference number.">
            {(p) => <Input {...p} {...fields.reference} maxLength={60} autoComplete="off" className="font-mono" />}
          </Field>
        </>
      )}
      <Field label="Received" error={errors.receivedAt} hint="Asia/Manila time.">
        {(p) => <Input {...p} {...fields.receivedAt} type="datetime-local" />}
      </Field>
    </div>
  )
}
