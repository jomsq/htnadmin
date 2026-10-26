import { useId, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import { useController, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { useAuth } from '@/app/auth/auth-context'
import { errorMessage } from '@/app/errors'
import { contrastWithWhite, DEFAULT_BRAND, isHexColor, MIN_CONTRAST } from '@/app/theme/brand'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { ColorSwatches } from '@/components/ui/ColorSwatches'
import { Field } from '@/components/ui/Field'
import { Checkbox, Input, Select } from '@/components/ui/Input'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import { useEventQuery } from '@/features/events/api'
import type { EventDetail } from '@/features/events/schemas'
import { useOrgSettingsQuery, usePaymentAccountsQuery } from '@/features/settings/api'
import type { Branding, PaymentAccount } from '@/features/settings/schemas'
import { useEmailSettingsQuery, useSaveEmailSettingsMutation, useSendTestEmailMutation } from './api'
import { EmailPreview } from './components/EmailPreview'
import { emailSubject, SAMPLE } from './format'
import {
  deadlineChoices,
  deadlineHours,
  emailSettingsFormSchema,
  type DeadlineChoice,
  type EmailSettings,
  type EmailSettingsFormValues,
  type EmailTemplate,
} from './schemas'

const templateOptions = [
  { value: 'billing', label: '1 · Billing' },
  { value: 'ticket', label: '2 · Ticket (after paid)' },
] as const

/** Billing & ticket email settings for one event (canvas frame 11). Route gated on Events.Manage. */
export function EmailSettingsPage() {
  const { eventId = '' } = useParams()
  const eventQuery = useEventQuery(eventId)
  const settingsQuery = useEmailSettingsQuery(eventId)
  const accountsQuery = usePaymentAccountsQuery(false)
  const orgQuery = useOrgSettingsQuery()

  const queries = [settingsQuery, accountsQuery, orgQuery, eventQuery]
  const failed = queries.find((q) => q.isError)
  if (failed) {
    return (
      <Alert
        tone="red"
        live="alert"
        action={
          <Button variant="secondary" size="sm" onClick={() => queries.filter((q) => q.isError).forEach((q) => void q.refetch())}>
            Try again
          </Button>
        }
      >
        Could not load the email settings. {errorMessage(failed.error)}
      </Alert>
    )
  }
  if (!settingsQuery.data || !accountsQuery.data || !orgQuery.data || !eventQuery.data) {
    return (
      <div role="status" aria-label="Loading email settings" className="flex flex-wrap gap-6">
        <Skeleton className="h-[480px] flex-[1_1_340px]" />
        <Skeleton className="h-[480px] flex-[999_1_520px]" />
      </div>
    )
  }
  return (
    <EmailSettingsForm
      event={eventQuery.data}
      settings={settingsQuery.data}
      accounts={accountsQuery.data.filter((a) => a.isActive)}
      branding={orgQuery.data.branding}
    />
  )
}

function initialValues(settings: EmailSettings, accounts: PaymentAccount[], branding: Branding): EmailSettingsFormValues {
  const known = new Set(accounts.map((a) => a.id))
  const chosen = settings.paymentAccountIds.filter((id) => known.has(id))
  const hours = settings.paymentDeadlineHours ?? branding.paymentDeadlineHours ?? 48
  const preset = (deadlineChoices as readonly string[]).includes(String(hours))
  return {
    accentColor: (settings.accentColor ?? branding.brandColor ?? DEFAULT_BRAND).toUpperCase(),
    paymentAccountIds: chosen.length ? chosen : accounts.filter((a) => a.isDefault).map((a) => a.id),
    deadline: preset ? (String(hours) as DeadlineChoice) : 'custom',
    customHours: preset ? '' : String(hours),
  }
}

function EmailSettingsForm({
  event,
  settings,
  accounts,
  branding,
}: {
  event: EventDetail
  settings: EmailSettings
  accounts: PaymentAccount[]
  branding: Branding
}) {
  const formId = useId()
  const { me } = useAuth()
  const { toast } = useToast()
  const save = useSaveEmailSettingsMutation(event.id)
  const test = useSendTestEmailMutation(event.id)
  const [template, setTemplate] = useState<EmailTemplate>('billing')
  const schema = useMemo(() => emailSettingsFormSchema(accounts.length > 0), [accounts.length])

  const {
    register,
    control,
    handleSubmit,
    setValue,
    setError,
    reset,
    formState: { errors, isDirty },
  } = useForm<EmailSettingsFormValues>({ resolver: zodResolver(schema), defaultValues: initialValues(settings, accounts, branding) })
  const picked = useController({ control, name: 'paymentAccountIds' })
  const accent = useWatch({ control, name: 'accentColor' }).trim()
  const deadline = useWatch({ control, name: 'deadline' })
  const customHours = useWatch({ control, name: 'customHours' })
  const validColor = isHexColor(accent)
  const ratio = validColor ? contrastWithWhite(accent) : null
  const lowContrast = ratio !== null && ratio < MIN_CONTRAST
  const previewHours = deadlineHours({ deadline, customHours }) ?? 48
  const selectedAccounts = accounts.filter((a) => picked.field.value.includes(a.id))
  const sampleTicket = event.tickets.find((t) => t.visibility === 'Public' && t.isActive && t.price > 0) ?? event.tickets[0]

  const onSubmit = handleSubmit((v) => {
    if (save.isPending) return
    const body: EmailSettings = { accentColor: v.accentColor.toUpperCase(), paymentAccountIds: v.paymentAccountIds, paymentDeadlineHours: deadlineHours(v) }
    save.mutate(body, {
      onSuccess: () => {
        reset(v)
        toast({ tone: 'success', title: 'Email settings saved.', description: 'New billing and ticket emails for this event use them.' })
      },
      onError: (e) => {
        const fe = isApiError(e, 400) ? e.fieldErrors : {}
        if (fe.accentColor) setError('accentColor', { type: 'server', message: fe.accentColor })
        else if (fe.paymentAccountIds) setError('paymentAccountIds', { type: 'server', message: fe.paymentAccountIds })
        else if (fe.paymentDeadlineHours) setError('customHours', { type: 'server', message: fe.paymentDeadlineHours })
        else toast({ tone: 'error', title: 'Could not save the email settings.', description: errorMessage(e) })
      },
    })
  })

  function onTest() {
    if (test.isPending) return
    test.mutate(template, {
      onSuccess: () =>
        toast({
          tone: 'success',
          title: `Test ${template} email sent${me?.email ? ` to ${me.email}` : ''}.`,
          description: isDirty ? 'It uses the saved settings. Save first to test your changes.' : undefined,
        }),
      onError: (e) => toast({ tone: 'error', title: 'Could not send the test email.', description: errorMessage(e) }),
    })
  }

  function toggleAccount(id: string, on: boolean) {
    const current = picked.field.value
    picked.field.onChange(on ? [...current, id] : current.filter((x) => x !== id))
  }

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-gray-600 dark:text-gray-400">The billing email goes out on registration; the ticket email with QR code once payment is confirmed.</p>
        <div className="flex flex-wrap gap-3">
          <Button variant="secondary" onClick={onTest} loading={test.isPending}>
            Send test email
          </Button>
          <Button type="submit" form={formId} loading={save.isPending} disabled={!isDirty || lowContrast}>
            Save email settings
          </Button>
        </div>
      </div>

      <div className="flex flex-wrap items-start gap-6">
        <Card aria-label="Template settings" className="flex min-w-0 flex-[1_1_340px] flex-col gap-5 p-5">
          <SegmentedControl aria-label="Template" options={templateOptions} value={template} onChange={setTemplate} fill />

          <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
            <div className="flex flex-col gap-2">
              <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Logo</span>
              <div className="flex items-center gap-3 rounded-md border border-dashed border-gray-300 p-3 dark:border-white/15">
                <div className="flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md bg-gray-900 text-xs font-bold text-white">
                  {branding.logoLightUrl ? <img src={branding.logoLightUrl} alt="" className="max-h-9 max-w-9 bg-white object-contain" /> : <span aria-hidden="true">HTN</span>}
                </div>
                <span className="flex-1 text-[0.8125rem] text-gray-600 dark:text-gray-400">{branding.logoLightUrl ? 'Organization logo' : 'No logo uploaded yet'}</span>
                <Link to="/settings?section=branding" className="inline-flex min-h-10 items-center text-[0.8125rem] font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300">
                  Change in Settings
                </Link>
              </div>
            </div>

            <fieldset className="flex flex-col gap-3">
              <legend className="mb-2 text-sm font-medium text-gray-700 dark:text-gray-200">Theme color</legend>
              <ColorSwatches aria-label="Theme color presets" value={accent} onChange={(hex) => setValue('accentColor', hex, { shouldDirty: true, shouldValidate: true })} />
              <div className="flex flex-wrap items-start gap-4">
                <Field label="Hex" error={errors.accentColor?.message} className="w-36">
                  {(p) => <Input {...p} {...register('accentColor')} maxLength={7} spellCheck={false} autoComplete="off" className="font-mono uppercase" />}
                </Field>
                <div aria-live="polite" className="flex min-h-10 flex-1 flex-col justify-end pt-7 text-[0.8125rem]">
                  {ratio !== null ? (
                    lowContrast ? (
                      <Alert tone="red">Low contrast: {ratio.toFixed(2)}:1 with white text. Email buttons would be hard to read. Pick a darker shade (4.5:1 or more).</Alert>
                    ) : (
                      <span className="flex items-center gap-2 text-gray-700 dark:text-gray-300">
                        <Badge color="green">Passes AA</Badge>
                        Contrast {ratio.toFixed(2)}:1
                      </span>
                    )
                  ) : null}
                </div>
              </div>
            </fieldset>

            <fieldset className="flex flex-col gap-2.5">
              <legend className="mb-2 text-sm font-semibold text-gray-900 dark:text-gray-50">Where to pay</legend>
              {accounts.length === 0 ? (
                <Alert tone="yellow">
                  No payment accounts yet.{' '}
                  <Link to="/settings?section=payments" className="font-medium underline">
                    Add one in Settings
                  </Link>{' '}
                  so billing emails show where to pay.
                </Alert>
              ) : (
                accounts.map((a) => (
                  <label key={a.id} className="flex min-h-11 items-center gap-3 rounded-md px-3 ring-1 ring-gray-200 ring-inset dark:ring-white/10">
                    <Checkbox checked={picked.field.value.includes(a.id)} onChange={(e) => toggleAccount(a.id, e.target.checked)} className="size-[18px]" />
                    <span className="flex min-w-0 flex-1 flex-col py-2 text-sm">
                      <span className="font-medium text-gray-900 dark:text-gray-50">{a.label}</span>
                      <span className="text-xs text-gray-600 dark:text-gray-400">
                        {a.accountName}
                        {a.accountNumberMasked ? <span className="font-mono"> · {a.accountNumberMasked}</span> : null}
                      </span>
                    </span>
                  </label>
                ))
              )}
              {errors.paymentAccountIds?.message ? (
                <p className="text-xs font-medium text-red-700 dark:text-red-400">{errors.paymentAccountIds.message}</p>
              ) : null}
            </fieldset>

            <div className="flex flex-wrap items-start gap-4">
              <Field label="Payment deadline" className="flex-[1_1_200px]">
                {(p) => (
                  <Select {...p} {...register('deadline')}>
                    <option value="24">24 hours after registering</option>
                    <option value="48">48 hours after registering</option>
                    <option value="72">72 hours after registering</option>
                    <option value="custom">Custom</option>
                  </Select>
                )}
              </Field>
              {deadline === 'custom' ? (
                <Field label="Hours" error={errors.customHours?.message} hint="1 to 336" className="w-28">
                  {(p) => <Input {...p} {...register('customHours')} inputMode="numeric" maxLength={3} className="tabular-nums" />}
                </Field>
              ) : null}
            </div>
          </form>

          <p className="text-xs leading-normal text-gray-600 dark:text-gray-400">
            Only these fields change between events. The layout, merge fields ({'{{first_name}}'}, ticket, amount, reference) and accessibility are fixed so every
            email stays readable.
          </p>
        </Card>

        <section aria-label="Preview" className="flex min-w-0 flex-[999_1_520px] flex-col gap-2.5">
          <div className="flex flex-wrap justify-between gap-2 text-[0.8125rem] text-gray-600 dark:text-gray-400">
            <span>Preview · to {SAMPLE.email}</span>
            <span>Subject: {emailSubject(template, event.name)}</span>
          </div>
          <EmailPreview
            template={template}
            accent={validColor && !lowContrast ? accent : DEFAULT_BRAND}
            logoUrl={branding.logoLightUrl}
            eventName={event.name}
            startsAt={event.startsAt}
            venue={event.venue}
            ticketName={sampleTicket?.name ?? 'Regular'}
            amount={sampleTicket?.price ?? 1500}
            proofMode={event.proofMode}
            deadlineHours={previewHours}
            accounts={selectedAccounts}
          />
        </section>
      </div>
    </>
  )
}
