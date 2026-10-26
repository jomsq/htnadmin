import { useId, useState, type ReactNode } from 'react'
import { Controller, useForm, useWatch, type Control } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { Switch } from '@/components/ui/Switch'
import { useToast } from '@/components/ui/toast-context'
import { formatPeso } from '@/lib/format'
import { usePaymentAccountsQuery } from '@/features/settings/api'
import { useUpdateEventMutation } from '../api'
import {
  parseAmount,
  parseInteger,
  registrationFormSchema,
  updateBodyFrom,
  type EventDetail,
  type ProofMode,
  type RegistrationBody,
  type RegistrationFormInput,
} from '../schemas'

type ToggleName = 'manualRegistration' | 'groupRegistration' | 'publicPage' | 'isMainEvent'

const proofOptions = [
  { value: 'Upload', label: 'Upload page' },
  { value: 'ReplyEmail', label: 'Reply to email' },
] as const satisfies ReadonlyArray<{ value: ProofMode; label: string }>

const proofHint: Record<ProofMode, string> = {
  Upload: 'The billing email links to a private page where they upload a screenshot and type the reference number.',
  ReplyEmail: 'The billing email asks them to reply with a screenshot of their payment.',
}

function toForm(e: EventDetail): RegistrationFormInput {
  return {
    manualRegistration: e.manualRegistration,
    groupRegistration: e.groupRegistration,
    publicPage: e.publicPage,
    isMainEvent: e.isMainEvent,
    membershipFee: e.membershipFee === null ? '' : String(e.membershipFee),
    membershipYear: e.membershipYear === null ? '' : String(e.membershipYear),
    proofMode: e.proofMode,
  }
}

function ToggleRow({
  control,
  name,
  label,
  help,
  disabled,
  children,
}: {
  control: Control<RegistrationFormInput, unknown, RegistrationBody>
  name: ToggleName
  label: string
  help: ReactNode
  disabled: boolean
  children?: ReactNode
}) {
  const labelId = useId()
  const helpId = useId()
  return (
    <div className="flex flex-col gap-3 border-t border-gray-100 py-3 dark:border-white/10">
      <div className="flex items-center gap-4">
        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
          <span id={labelId} className="text-sm font-medium text-gray-900 dark:text-gray-50">
            {label}
          </span>
          <span id={helpId} className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
            {help}
          </span>
        </div>
        <Controller
          control={control}
          name={name}
          render={({ field }) => (
            <Switch checked={field.value} onChange={field.onChange} disabled={disabled} aria-labelledby={labelId} aria-describedby={helpId} />
          )}
        />
      </div>
      {children}
    </div>
  )
}

/** Details › Registration & payment: toggles, main-event membership, proof mode and the payment accounts used. */
export function RegistrationSection({ event, canManage }: { event: EventDetail; canManage: boolean }) {
  const { toast } = useToast()
  const mutation = useUpdateEventMutation(event.id)
  const proofLabelId = useId()
  const proofHintId = useId()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    control,
    handleSubmit,
    setError,
    reset,
    formState: { errors, isDirty },
  } = useForm<RegistrationFormInput, unknown, RegistrationBody>({ resolver: zodResolver(registrationFormSchema), values: toForm(event) })
  const [isMainEvent, fee, year, proofMode] = useWatch({ control, name: ['isMainEvent', 'membershipFee', 'membershipYear', 'proofMode'] })

  const feeValue = parseAmount(fee)
  const yearValue = parseInteger(year)
  const membershipHelp = `Sells Membership ${yearValue ?? 'YYYY'}${feeValue !== null ? ` (${formatPeso(feeValue)})` : ''} at the venue to attendees who have checked in at this or a past event. Valid until the next main event.`

  const onSubmit = handleSubmit((values) => {
    if (mutation.isPending || !canManage) return
    setFormError(null)
    mutation.mutate(
      { ...updateBodyFrom(event), ...values },
      {
        onSuccess: (saved) => {
          reset(toForm(saved))
          toast({ tone: 'success', title: 'Registration settings saved.' })
        },
        onError: (e) => {
          const fields = isApiError(e, 400) ? e.fieldErrors : {}
          if (fields.membershipFee) setError('membershipFee', { type: 'server', message: fields.membershipFee })
          if (fields.membershipYear) setError('membershipYear', { type: 'server', message: fields.membershipYear })
          if (!fields.membershipFee && !fields.membershipYear) setFormError(errorMessage(e))
        },
      },
    )
  })

  return (
    <Card id="registration" aria-labelledby="registration-heading" className="scroll-mt-6">
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-1 p-6">
        <h2 id="registration-heading" className="pb-3 text-base font-semibold text-gray-900 dark:text-gray-50">
          Registration & payment
        </h2>
        {formError ? (
          <Alert tone="red" live="alert" className="mb-3">
            {formError}
          </Alert>
        ) : null}

        <ToggleRow control={control} name="manualRegistration" label="Manual registration" help="Admins can add attendees from the Registrations tab." disabled={!canManage} />
        <ToggleRow control={control} name="groupRegistration" label="Group registration" help="One buyer fills in details for every member of the group." disabled={!canManage} />
        <ToggleRow control={control} name="publicPage" label="Public registration page" help="Anyone with the link can register." disabled={!canManage} />
        <ToggleRow
          control={control}
          name="isMainEvent"
          label="Main event of the year: collect membership fees"
          help={membershipHelp}
          disabled={!canManage}
        >
          {isMainEvent ? (
            <fieldset disabled={!canManage} className="grid grid-cols-1 gap-3.5 sm:max-w-md sm:grid-cols-2">
              <legend className="sr-only">Membership</legend>
              <Field label="Membership fee (₱)" error={errors.membershipFee?.message}>
                {(p) => <Input {...p} {...register('membershipFee')} inputMode="decimal" className="tabular-nums" />}
              </Field>
              <Field label="Membership year" error={errors.membershipYear?.message}>
                {(p) => <Input {...p} {...register('membershipYear')} inputMode="numeric" className="tabular-nums" />}
              </Field>
            </fieldset>
          ) : null}
        </ToggleRow>

        <div className="flex flex-col gap-2 border-t border-gray-100 py-3 dark:border-white/10">
          <span id={proofLabelId} className="text-sm font-medium text-gray-900 dark:text-gray-50">
            Proof of payment
          </span>
          {canManage ? (
            <Controller
              control={control}
              name="proofMode"
              render={({ field }) => (
                <SegmentedControl
                  options={proofOptions}
                  value={field.value}
                  onChange={field.onChange}
                  aria-labelledby={proofLabelId}
                  aria-describedby={proofHintId}
                  className="w-fit"
                />
              )}
            />
          ) : (
            <span className="text-sm text-gray-700 dark:text-gray-300">{proofOptions.find((o) => o.value === proofMode)?.label}</span>
          )}
          <span id={proofHintId} className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
            {proofHint[proofMode]}
          </span>
        </div>

        {canManage ? <PaymentMethodsRow /> : null}

        {canManage ? (
          <div className="flex justify-end pt-3">
            <Button type="submit" loading={mutation.isPending} disabled={!isDirty}>
              Save registration settings
            </Button>
          </div>
        ) : null}
      </form>
    </Card>
  )
}

/** Payment accounts from Settings, shown in billing emails (per-event overrides live on the Emails tab). */
function PaymentMethodsRow() {
  const query = usePaymentAccountsQuery(false)
  const accounts = query.data?.filter((a) => a.isActive) ?? []
  return (
    <div className="flex flex-wrap items-center gap-3 border-t border-gray-100 pt-3 pb-1 dark:border-white/10">
      <span className="flex-[1_1_200px] text-sm font-medium text-gray-900 dark:text-gray-50">Payment methods (manual verification)</span>
      {query.isError ? (
        <span className="text-[0.8125rem] text-red-700 dark:text-red-400">Could not load payment accounts.</span>
      ) : query.isSuccess && accounts.length === 0 ? (
        <span className="text-[0.8125rem] text-yellow-800 dark:text-yellow-300">No payment accounts yet.</span>
      ) : (
        <ul aria-label="Payment accounts" className="flex flex-wrap gap-2">
          {accounts.map((a) => (
            <li key={a.id} className="rounded-md px-3 py-1.5 text-[0.8125rem] font-semibold text-gray-900 ring-1 ring-gray-300 ring-inset dark:text-gray-50 dark:ring-white/15">
              {a.label}
              {a.accountNumberMasked ? <span className="font-mono font-normal text-gray-600 dark:text-gray-400"> · {a.accountNumberMasked}</span> : null}
            </li>
          ))}
        </ul>
      )}
      <Link to="/settings?section=payments" className="inline-flex min-h-10 items-center text-[0.8125rem] font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300">
        Manage accounts
      </Link>
    </div>
  )
}
