import type { FieldErrors, UseFormRegister } from 'react-hook-form'
import { Field } from '@/components/ui/Field'
import { Input, Textarea } from '@/components/ui/Input'
import type { EventInfoFormInput } from '../schemas'

type Props = {
  register: UseFormRegister<EventInfoFormInput>
  errors: FieldErrors<EventInfoFormInput>
  /** Shown under the page link field, e.g. "/r/htn-con-2026". */
  slugHint: string
  slugLocked?: boolean
}

/** Event name, page link, start/end (Asia/Manila), venue, capacity and description (canvas frame 05, Event info). */
export function EventInfoFields({ register, errors, slugHint, slugLocked = false }: Props) {
  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(min(240px,100%),1fr))] gap-4">
      <Field label="Event name" error={errors.name?.message} className="col-span-full">
        {(p) => <Input {...p} {...register('name')} controlSize="lg" autoComplete="off" />}
      </Field>
      <Field label="Registration page link" optional={!slugLocked} hint={slugHint} error={errors.slug?.message} className="col-span-full">
        {(p) => (
          <div className="flex items-center gap-2">
            <span aria-hidden="true" className="font-mono text-sm text-gray-600 dark:text-gray-400">
              /r/
            </span>
            <Input
              {...p}
              {...register('slug')}
              controlSize="lg"
              readOnly={slugLocked}
              spellCheck={false}
              autoComplete="off"
              placeholder="htn-con-2026"
              className="font-mono lowercase"
            />
          </div>
        )}
      </Field>
      <Field label="Start" hint="Philippine time" error={errors.startsAt?.message}>
        {(p) => <Input {...p} {...register('startsAt')} type="datetime-local" controlSize="lg" />}
      </Field>
      <Field label="End" hint="Philippine time" error={errors.endsAt?.message}>
        {(p) => <Input {...p} {...register('endsAt')} type="datetime-local" controlSize="lg" />}
      </Field>
      <Field label="Venue" error={errors.venue?.message}>
        {(p) => <Input {...p} {...register('venue')} controlSize="lg" autoComplete="off" />}
      </Field>
      <Field label="Capacity" error={errors.capacity?.message}>
        {(p) => <Input {...p} {...register('capacity')} inputMode="numeric" controlSize="lg" className="tabular-nums" />}
      </Field>
      <Field label="Description" optional hint="Short description shown on the registration page." error={errors.description?.message} className="col-span-full">
        {(p) => <Textarea {...p} {...register('description')} rows={3} />}
      </Field>
    </div>
  )
}
