import { useId, useState } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Checkbox, Input } from '@/components/ui/Input'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { useToast } from '@/components/ui/toast-context'
import { useSaveVoucherMutation } from '../api'
import { toManilaInput } from '../format'
import { voucherFormSchema, type TicketType, type Voucher, type VoucherBody, type VoucherFormInput, type VoucherKind } from '../schemas'

const fieldNames = ['code', 'kind', 'value', 'ticketTypeIds', 'usageLimit', 'expiresAt'] as const
type FieldName = (typeof fieldNames)[number]
const isFieldName = (k: string): k is FieldName => (fieldNames as readonly string[]).includes(k)

const kindOptions = [
  { value: 'Percent', label: '% off' },
  { value: 'Fixed', label: '₱ off' },
] as const satisfies ReadonlyArray<{ value: VoucherKind; label: string }>

/** Add or edit a voucher. Codes are stored upper-case and are unique per event. */
export function VoucherDialog({
  eventId,
  voucher,
  tickets,
  onClose,
}: {
  eventId: string
  voucher: Voucher | null
  tickets: readonly TicketType[]
  onClose: () => void
}) {
  const formId = useId()
  const kindLabelId = useId()
  const { toast } = useToast()
  const mutation = useSaveVoucherMutation(eventId)
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<VoucherFormInput, unknown, VoucherBody>({
    resolver: zodResolver(voucherFormSchema),
    defaultValues: {
      code: voucher?.code ?? '',
      kind: voucher?.kind ?? 'Percent',
      value: voucher ? String(voucher.value) : '',
      ticketTypeIds: voucher?.ticketTypeIds ?? [],
      usageLimit: voucher?.usageLimit != null ? String(voucher.usageLimit) : '',
      expiresAt: toManilaInput(voucher?.expiresAt),
    },
  })
  const kind = useWatch({ control, name: 'kind' })
  const choices = tickets.filter((t) => t.isActive || voucher?.ticketTypeIds.includes(t.id))

  const onSubmit = handleSubmit((body) => {
    if (mutation.isPending) return
    setFormError(null)
    mutation.mutate(
      { voucherId: voucher?.id, body },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: voucher ? `${body.code} saved.` : `${body.code} added.` })
          onClose()
        },
        onError: (e) => {
          if (isApiError(e) && e.code === 'duplicate_code') {
            setError('code', { type: 'server', message: errorMessage(e) })
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
      title={voucher ? `Edit ${voucher.code}` : 'Add voucher'}
      description="Usage counts registrations that are not cancelled."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={mutation.isPending}>
            {voucher ? 'Save voucher' : 'Add voucher'}
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
        <Field label="Code" error={errors.code?.message} hint="Letters, numbers and dashes, like STUDENT20.">
          {(p) => <Input {...p} {...register('code')} autoComplete="off" spellCheck={false} className="font-mono uppercase" />}
        </Field>
        <div className="grid grid-cols-1 items-start gap-3.5 sm:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <span id={kindLabelId} className="text-sm font-medium text-gray-700 dark:text-gray-200">
              Discount type
            </span>
            <Controller
              control={control}
              name="kind"
              render={({ field }) => (
                <SegmentedControl options={kindOptions} value={field.value} onChange={field.onChange} aria-labelledby={kindLabelId} className="w-fit" />
              )}
            />
          </div>
          <Field label={kind === 'Percent' ? 'Percent off' : 'Amount off (₱)'} error={errors.value?.message} hint={kind === 'Percent' ? '1 to 100.' : undefined}>
            {(p) => <Input {...p} {...register('value')} inputMode="decimal" className="tabular-nums" />}
          </Field>
        </div>
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-sm font-medium text-gray-700 dark:text-gray-200">Applies to</legend>
          {choices.length === 0 ? (
            <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">All tickets. Add ticket types to limit it to some of them.</p>
          ) : (
            <>
              <div className="flex flex-wrap gap-x-5 gap-y-1">
                {choices.map((t) => (
                  <label key={t.id} className="flex min-h-10 items-center gap-2.5 text-sm text-gray-700 dark:text-gray-200">
                    <Checkbox value={t.id} {...register('ticketTypeIds')} />
                    {t.name}
                  </label>
                ))}
              </div>
              <p className="text-xs text-gray-600 dark:text-gray-400">Leave all unchecked to apply it to every ticket.</p>
            </>
          )}
        </fieldset>
        <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">
          <Field label="Usage limit" optional error={errors.usageLimit?.message} hint="Leave empty for no limit.">
            {(p) => <Input {...p} {...register('usageLimit')} inputMode="numeric" className="tabular-nums" />}
          </Field>
          <Field label="Expires" optional error={errors.expiresAt?.message} hint="Philippine time.">
            {(p) => <Input {...p} {...register('expiresAt')} type="datetime-local" />}
          </Field>
        </div>
      </form>
    </Dialog>
  )
}
