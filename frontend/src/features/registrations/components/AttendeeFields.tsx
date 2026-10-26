import { useId } from 'react'
import { useController, type Control, type FieldErrors, type UseFormRegister } from 'react-hook-form'
import { Field } from '@/components/ui/Field'
import { Input, Select, type ControlSize } from '@/components/ui/Input'
import { PrefixInput } from '@/components/ui/PrefixInput'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import type { FormOptions, RegistrationFormValues } from '../schemas'

type Props = {
  register: UseFormRegister<RegistrationFormValues>
  control: Control<RegistrationFormValues>
  errors: FieldErrors<RegistrationFormValues>
  options: FormOptions
  eventName: string
  size?: ControlSize
  /** Public form: "Billing and your ticket are sent here." Manual: about the attendee. */
  audience: 'self' | 'admin'
}

/** Buyer details in the order of the htn-domain form fields (canvas frame 10). */
export function BuyerFields({ register, control, errors, options, eventName, size = 'md', audience }: Props) {
  const e = errors.buyer
  const shirt = useController({ control, name: 'buyer.shirtSize' })
  const shirtId = useId()
  return (
    <>
      <Field label="First name" error={e?.firstName?.message}>
        {(p) => <Input {...p} {...register('buyer.firstName')} controlSize={size} autoComplete="given-name" maxLength={60} />}
      </Field>
      <Field label="Middle name" optional error={e?.middleName?.message}>
        {(p) => <Input {...p} {...register('buyer.middleName')} controlSize={size} autoComplete="additional-name" maxLength={60} />}
      </Field>
      <Field label="Last name" error={e?.lastName?.message}>
        {(p) => <Input {...p} {...register('buyer.lastName')} controlSize={size} autoComplete="family-name" maxLength={60} />}
      </Field>
      {options.genders.length ? (
        <Field label="Gender" error={e?.gender?.message}>
          {(p) => (
            <Select {...p} {...register('buyer.gender')} controlSize={size} autoComplete="sex">
              <option value="">Select</option>
              {options.genders.map((g) => (
                <option key={g} value={g}>
                  {g}
                </option>
              ))}
            </Select>
          )}
        </Field>
      ) : null}
      <Field
        label="Email address"
        error={e?.email?.message}
        hint={audience === 'self' ? 'Billing and your ticket are sent here.' : 'Billing and the ticket are sent here.'}
      >
        {(p) => <Input {...p} {...register('buyer.email')} controlSize={size} type="email" autoComplete="email" inputMode="email" maxLength={254} />}
      </Field>
      <Field label="Mobile number" error={e?.mobile?.message} hint="Philippine mobile number after +63, like 917 123 4567.">
        {(p) => (
          <PrefixInput
            {...p}
            {...register('buyer.mobile')}
            prefix="+63"
            controlSize={size}
            type="tel"
            autoComplete="tel-national"
            inputMode="tel"
            placeholder="917 123 4567"
            maxLength={16}
          />
        )}
      </Field>
      <Field label="Occupation" optional error={e?.occupation?.message}>
        {(p) => <Input {...p} {...register('buyer.occupation')} controlSize={size} autoComplete="organization-title" maxLength={120} />}
      </Field>
      <Field label="Agency / company" optional error={e?.company?.message}>
        {(p) => <Input {...p} {...register('buyer.company')} controlSize={size} autoComplete="organization" maxLength={120} />}
      </Field>
      {options.shirtSizes.length ? (
        <div className="flex flex-col gap-2">
          <span id={`${shirtId}-label`} className="text-sm font-medium text-gray-700 dark:text-gray-200">
            Shirt size <span className="font-normal text-gray-600 dark:text-gray-400">(optional)</span>
          </span>
          <SegmentedControl
            aria-labelledby={`${shirtId}-label`}
            aria-describedby={`${shirtId}-hint`}
            variant="chips"
            size="lg"
            fill
            options={options.shirtSizes.map((s) => ({ value: s, label: s }))}
            value={shirt.field.value}
            onChange={(v) => shirt.field.onChange(v)}
          />
          <span id={`${shirtId}-hint`} className="text-xs text-gray-600 dark:text-gray-400">
            For headcount only. Shirts are not included with the ticket.
          </span>
          {e?.shirtSize?.message ? <span className="text-xs font-medium text-red-700 dark:text-red-400">{e.shirtSize.message}</span> : null}
        </div>
      ) : null}
      {options.hearAbout.length ? (
        <Field label={`How did you hear about ${eventName}?`} optional error={e?.hearAbout?.message}>
          {(p) => (
            <Select {...p} {...register('buyer.hearAbout')} controlSize={size}>
              <option value="">Select</option>
              {options.hearAbout.map((h) => (
                <option key={h} value={h}>
                  {h}
                </option>
              ))}
            </Select>
          )}
        </Field>
      ) : null}
    </>
  )
}

/** One compact card per extra group member: first name, last name, email. */
export function MemberCard({
  index,
  groupSize,
  register,
  errors,
  size = 'md',
}: {
  index: number
  groupSize: number
  register: UseFormRegister<RegistrationFormValues>
  errors: FieldErrors<RegistrationFormValues>
  size?: ControlSize
}) {
  const e = errors.members?.[index]
  return (
    <fieldset className="flex flex-col gap-3 rounded-lg p-4 ring-1 ring-gray-200 dark:ring-white/10">
      <legend className="sr-only">
        Teammate {index + 1} of {groupSize - 1}
      </legend>
      <p aria-hidden="true" className="text-sm font-semibold text-gray-900 dark:text-gray-50">
        Teammate {index + 1}
      </p>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="First name" error={e?.firstName?.message}>
          {(p) => <Input {...p} {...register(`members.${index}.firstName`)} controlSize={size} autoComplete="off" maxLength={60} />}
        </Field>
        <Field label="Last name" error={e?.lastName?.message}>
          {(p) => <Input {...p} {...register(`members.${index}.lastName`)} controlSize={size} autoComplete="off" maxLength={60} />}
        </Field>
      </div>
      <Field label="Email address" error={e?.email?.message}>
        {(p) => <Input {...p} {...register(`members.${index}.email`)} controlSize={size} type="email" inputMode="email" autoComplete="off" maxLength={254} />}
      </Field>
    </fieldset>
  )
}
