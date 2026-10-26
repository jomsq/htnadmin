import { useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useFieldArray, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { isApiError } from '@/app/api'
import { errorMessage, messageForCode, RATE_LIMITED_MESSAGE } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Field } from '@/components/ui/Field'
import { Checkbox, Input } from '@/components/ui/Input'
import { RadioCard } from '@/components/ui/RadioCard'
import { ChevronLeftIcon } from '@/components/ui/icons'
import { cn } from '@/lib/cn'
import { formatPeso } from '@/lib/format'
import { randomUuid } from '@/lib/uuid'
import { registrationKeys, useSubmitRegistrationMutation, useVoucherCheckMutation } from '../api'
import { BuyerFields, MemberCard } from '../components/AttendeeFields'
import { applyRegistrationFieldErrors } from '../components/serverErrors'
import { publicTickets } from '../helpers'
import {
  emptyRegistrationValues,
  registrationFormSchema,
  resizeMembers,
  toAttendees,
  voucherOrUndefined,
  type PublicEvent,
  type PublicTicket,
  type RegistrationFormValues,
  type RegistrationResult,
} from '../schemas'

const INVALID_VOUCHER = messageForCode('invalid_voucher') ?? "This voucher code can't be used."

type Voucher = { state: 'applied'; code: string; discount: number; total: number } | { state: 'invalid' } | null

function ticketNote(t: PublicTicket): string {
  if (t.remaining <= 0) return 'Sold out'
  const parts = [t.note, t.groupSize > 1 && !t.note ? `Add ${t.groupSize - 1} teammates on the next step` : null, `${t.remaining} left`]
  return parts.filter(Boolean).join(' · ')
}

/** Public registration form (canvas frame 10): ticket, your details, teammates (group tickets), voucher, totals, consent. */
export function PublicRegistrationForm({ slug, event, onDone }: { slug: string; event: PublicEvent; onDone: (r: RegistrationResult) => void }) {
  const queryClient = useQueryClient()
  // One key per filled-in form: a retried or double submit can't create a second registration.
  const [idempotencyKey] = useState(randomUuid)
  const tickets = publicTickets(event)
  const firstOpen = tickets.find((t) => t.remaining > 0)
  const allSoldOut = !firstOpen
  const options = useMemo(() => ({ genders: event.genders, hearAbout: event.hearAbout, shirtSizes: event.shirtSizes }), [event])
  const schema = useMemo(() => registrationFormSchema({ ...options, mode: 'public' }), [options])
  const [step, setStep] = useState<'details' | 'members'>('details')
  const [voucher, setVoucher] = useState<Voucher>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const stepHeading = useRef<HTMLHeadingElement>(null)
  const voucherCheck = useVoucherCheckMutation(slug)
  const submit = useSubmitRegistrationMutation(slug)

  const {
    register,
    control,
    handleSubmit,
    trigger,
    getValues,
    setError,
    clearErrors,
    formState: { errors },
  } = useForm<RegistrationFormValues>({
    resolver: zodResolver(schema),
    defaultValues: emptyRegistrationValues(firstOpen?.id ?? '', firstOpen?.groupSize ?? 1),
  })
  const members = useFieldArray({ control, name: 'members' })
  const ticketTypeId = useWatch({ control, name: 'ticketTypeId' })
  const ticket = tickets.find((t) => t.id === ticketTypeId)
  const groupSize = ticket?.groupSize ?? 1
  const isGroup = groupSize > 1
  const onFinalStep = !isGroup || step === 'members'

  const subtotal = ticket?.price ?? 0
  const applied = voucher?.state === 'applied' ? voucher : null
  const total = applied ? applied.total : subtotal

  function goTo(next: 'details' | 'members') {
    setStep(next)
    // Move focus to the new step's heading so screen reader and keyboard users land at the top.
    requestAnimationFrame(() => stepHeading.current?.focus())
  }

  const ticketField = register('ticketTypeId', {
    onChange: (e: { target: { value: string } }) => {
      const next = tickets.find((t) => t.id === e.target.value)
      members.replace(resizeMembers(getValues('members'), next?.groupSize ?? 1))
      setVoucher(null)
      setFormError(null)
    },
  })

  const voucherField = register('voucherCode', { onChange: () => voucher && setVoucher(null) })

  function applyVoucher() {
    const code = voucherOrUndefined(getValues('voucherCode'))
    if (!code) {
      setError('voucherCode', { type: 'manual', message: 'Enter a voucher code.' })
      return
    }
    if (!ticket || voucherCheck.isPending) return
    clearErrors('voucherCode')
    voucherCheck.mutate(
      { code, ticketTypeId: ticket.id },
      {
        onSuccess: (r) => setVoucher(r.valid ? { state: 'applied', code, discount: r.discount, total: r.total } : { state: 'invalid' }),
        onError: (e) => {
          if (isApiError(e, 429)) setError('voucherCode', { type: 'server', message: RATE_LIMITED_MESSAGE })
          else setVoucher({ state: 'invalid' })
        },
      },
    )
  }

  function onVoucherKey(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key !== 'Enter') return
    e.preventDefault()
    applyVoucher()
  }

  async function onContinue() {
    const ok = await trigger(['ticketTypeId', 'buyer'], { shouldFocus: true })
    if (ok) goTo('members')
  }

  const onSubmit = handleSubmit((values) => {
    if (submit.isPending) return
    setFormError(null)
    submit.mutate(
      {
        idempotencyKey,
        ticketTypeId: values.ticketTypeId,
        voucherCode: voucherOrUndefined(values.voucherCode),
        attendees: toAttendees(values),
        consent: true,
        website: values.website,
      },
      {
        onSuccess: onDone,
        onError: (e) => {
          const paths = applyRegistrationFieldErrors(e, setError)
          if (paths.length) {
            if (paths.some((p) => p.startsWith('buyer') || p === 'ticketTypeId')) setStep('details')
            return
          }
          if (isApiError(e) && e.code === 'invalid_voucher') {
            setVoucher({ state: 'invalid' })
            return
          }
          if (isApiError(e) && (e.code === 'sold_out' || e.code === 'ticket_unavailable')) {
            setStep('details')
            void queryClient.invalidateQueries({ queryKey: registrationKeys.publicEvent(slug) })
          }
          setFormError(isApiError(e, 404) ? "Registration isn't open anymore." : errorMessage(e))
        },
      },
    )
  })

  return (
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-6 px-5 pt-6 pb-8">
      {isGroup ? (
        <p className="text-[0.8125rem] font-medium text-gray-600 dark:text-gray-400">
          Step {step === 'details' ? 1 : 2} of 2 · {step === 'details' ? 'Your details' : 'Your teammates'}
        </p>
      ) : null}

      {formError ? (
        <Alert tone="red" live="alert">
          {formError}
        </Alert>
      ) : null}

      {step === 'details' || !isGroup ? (
        <>
          <fieldset className="flex flex-col gap-2.5">
            <legend className="mb-2.5 text-base font-semibold text-gray-900 dark:text-gray-50">Ticket</legend>
            {allSoldOut ? <Alert tone="yellow">All tickets are sold out.</Alert> : null}
            {tickets.map((t) => (
              <RadioCard
                key={t.id}
                {...ticketField}
                value={t.id}
                checked={ticketTypeId === t.id}
                disabled={t.remaining <= 0}
                title={t.name}
                description={ticketNote(t)}
                aside={formatPeso(t.price)}
              />
            ))}
            {errors.ticketTypeId?.message ? <p className="text-xs font-medium text-red-700 dark:text-red-400">{errors.ticketTypeId.message}</p> : null}
          </fieldset>

          <fieldset className="flex flex-col gap-3.5">
            <legend className="mb-3.5 text-base font-semibold text-gray-900 dark:text-gray-50">
              <h2 ref={step === 'details' ? stepHeading : undefined} tabIndex={-1} className="outline-none">
                Your details
              </h2>
            </legend>
            <BuyerFields register={register} control={control} errors={errors} options={options} eventName={event.name} size="xl" audience="self" />
          </fieldset>
        </>
      ) : (
        <fieldset className="flex flex-col gap-3">
          <legend className="mb-1 flex w-full flex-col gap-1">
            <h2 ref={stepHeading} tabIndex={-1} className="text-base font-semibold text-gray-900 outline-none dark:text-gray-50">
              Your teammates
            </h2>
          </legend>
          <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
            {ticket?.name} is for {groupSize} people. Add the other {groupSize - 1}; each gets their own ticket by email.
          </p>
          {members.fields.map((f, i) => (
            <MemberCard key={f.id} index={i} groupSize={groupSize} register={register} errors={errors} size="xl" />
          ))}
        </fieldset>
      )}

      {/* Honeypot for bots: people never see or reach it. */}
      <div aria-hidden="true" className="absolute -left-[10000px] h-px w-px overflow-hidden">
        <label>
          Website
          <input type="text" tabIndex={-1} autoComplete="off" aria-hidden="true" {...register('website')} />
        </label>
      </div>

      {onFinalStep ? (
        <>
          <div className="flex flex-col gap-1.5">
            <Field label="Voucher code" optional error={errors.voucherCode?.message}>
              {(p) => (
                <div className="flex gap-2">
                  <Input
                    {...p}
                    {...voucherField}
                    onKeyDown={onVoucherKey}
                    controlSize="xl"
                    maxLength={30}
                    autoComplete="off"
                    autoCapitalize="characters"
                    spellCheck={false}
                    className="min-w-0 flex-1 uppercase"
                  />
                  <Button variant="secondary" className="h-12 shrink-0" onClick={applyVoucher} loading={voucherCheck.isPending}>
                    Apply
                  </Button>
                </div>
              )}
            </Field>
            <p aria-live="polite" className={cn('text-[0.8125rem] font-medium', applied ? 'text-green-700 dark:text-green-400' : 'text-red-700 dark:text-red-400')}>
              {applied ? `${applied.code} applied · ${formatPeso(applied.discount)} off` : voucher?.state === 'invalid' ? INVALID_VOUCHER : ''}
            </p>
          </div>

          <dl className="flex flex-col gap-1.5 border-t border-gray-200 pt-4 text-sm dark:border-white/10">
            <div className="flex justify-between text-gray-600 dark:text-gray-400">
              <dt>{ticket ? `${ticket.name} ticket` : 'Ticket'}</dt>
              <dd className="tabular-nums">{formatPeso(subtotal)}</dd>
            </div>
            {applied ? (
              <div className="flex justify-between text-green-700 dark:text-green-400">
                <dt>Voucher</dt>
                <dd className="tabular-nums">−{formatPeso(applied.discount)}</dd>
              </div>
            ) : null}
            <div className="flex justify-between pt-1.5 text-lg font-semibold text-gray-900 dark:text-gray-50">
              <dt>Total</dt>
              <dd className="tabular-nums">{formatPeso(total)}</dd>
            </div>
          </dl>

          <div className="flex flex-col gap-1.5">
            <label className="flex min-h-11 items-start gap-2.5 text-[0.8125rem] leading-normal text-gray-700 dark:text-gray-300">
              <Checkbox
                {...register('consent')}
                aria-invalid={errors.consent ? true : undefined}
                aria-describedby={errors.consent ? 'consent-error' : undefined}
                className="mt-0.5 size-[18px] shrink-0"
              />
              <span>{event.consentText}</span>
            </label>
            {errors.consent?.message ? (
              <p id="consent-error" className="text-xs font-medium text-red-700 dark:text-red-400">
                {errors.consent.message}
              </p>
            ) : null}
          </div>

          <div className="flex flex-col gap-3">
            <Button type="submit" size="lg" className="h-13 text-base" loading={submit.isPending} disabled={allSoldOut}>
              Register & get billing details
            </Button>
            {isGroup ? (
              <Button variant="ghost" size="lg" onClick={() => goTo('details')}>
                <ChevronLeftIcon size={16} />
                Back to your details
              </Button>
            ) : null}
            <p className="text-center text-[0.8125rem] text-gray-600 dark:text-gray-400">Payment by GCash or bank transfer. No card needed.</p>
          </div>
        </>
      ) : (
        <Button size="lg" className="h-13 text-base" onClick={() => void onContinue()} disabled={allSoldOut}>
          Continue · add {groupSize - 1} teammates
        </Button>
      )}
    </form>
  )
}
