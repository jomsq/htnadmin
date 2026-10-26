import { useId, useState } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { useToast } from '@/components/ui/toast-context'
import { useCreatePersonMutation } from '../api'
import {
  accessNotes,
  createPersonSchema,
  levelLabels,
  levelSchema,
  type CreatePersonBody,
  type CreatePersonInput,
} from '../schemas'

const fieldNames = ['firstName', 'middleName', 'lastName', 'email', 'position', 'level'] as const
type FieldName = (typeof fieldNames)[number]
const isFieldName = (k: string): k is FieldName => (fieldNames as readonly string[]).includes(k)

const levelOptions = levelSchema.options.map((l) => ({ value: l, label: levelLabels[l] }))

const defaults: CreatePersonInput = { firstName: '', middleName: '', lastName: '', email: '', position: '', level: 'Apprentice' }

export function CreateAccountDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const formId = useId()
  const levelLabelId = useId()
  const { toast } = useToast()
  const mutation = useCreatePersonMutation()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<CreatePersonInput, unknown, CreatePersonBody>({ resolver: zodResolver(createPersonSchema), defaultValues: defaults })

  const level = useWatch({ control, name: 'level' })
  const isMember = level === 'Member'

  function close() {
    reset(defaults)
    setFormError(null)
    mutation.reset()
    onClose()
  }

  const onSubmit = handleSubmit((body) => {
    if (mutation.isPending) return
    setFormError(null)
    mutation.mutate(body, {
      onSuccess: () => {
        toast({
          tone: 'success',
          title: body.level === 'Member' ? 'Member added to the roster.' : 'Account created. Invite email sent; it expires in 48 hours.',
        })
        close()
      },
      onError: (e) => {
        let mapped = false
        if (isApiError(e, 400) || isApiError(e, 409)) {
          for (const [key, message] of Object.entries(e.fieldErrors)) {
            if (isFieldName(key)) {
              setError(key, { type: 'server', message })
              mapped = true
            }
          }
        }
        if (!mapped) setFormError(errorMessage(e, { 409: 'Someone with this email is already in the organization.' }))
      },
    })
  })

  return (
    <Dialog
      open={open}
      onClose={close}
      title="Create account"
      description="There is no public sign-up. Only admins add people."
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={mutation.isPending}>
            {isMember ? 'Add to roster' : 'Create & send invite'}
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
          <Field label="First name" error={errors.firstName?.message}>
            {(p) => <Input {...p} {...register('firstName')} autoComplete="off" />}
          </Field>
          <Field label="Middle name" optional error={errors.middleName?.message}>
            {(p) => <Input {...p} {...register('middleName')} autoComplete="off" />}
          </Field>
          <Field label="Last name" error={errors.lastName?.message}>
            {(p) => <Input {...p} {...register('lastName')} autoComplete="off" />}
          </Field>
          <Field label="Email" error={errors.email?.message}>
            {(p) => <Input {...p} {...register('email')} type="email" autoComplete="off" />}
          </Field>
          <Field label="Position" optional error={errors.position?.message} className="sm:col-span-2">
            {(p) => <Input {...p} {...register('position')} placeholder="e.g. Logistics" autoComplete="off" />}
          </Field>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <span id={levelLabelId} className="min-w-[60px] text-[0.8125rem] font-medium text-gray-700 dark:text-gray-200">
            Level
          </span>
          <Controller
            control={control}
            name="level"
            render={({ field }) => (
              <SegmentedControl variant="chips" options={levelOptions} value={field.value} onChange={field.onChange} aria-labelledby={levelLabelId} />
            )}
          />
        </div>
        <Alert tone="gray" live="status">
          {accessNotes[level]}
        </Alert>
      </form>
    </Dialog>
  )
}
