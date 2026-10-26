import { useId, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage, messageForCode } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useUpdatePersonMutation } from '../api'
import { editPersonSchema, type EditPersonInput, type EditPersonValues, type Person } from '../schemas'

const fieldNames = ['firstName', 'middleName', 'lastName', 'email', 'position'] as const
type FieldName = (typeof fieldNames)[number]
const isFieldName = (k: string): k is FieldName => (fieldNames as readonly string[]).includes(k)

/** Email can only change while there is no login or the invite is still pending. */
function emailLocked(p: Person): boolean {
  return p.accountStatus === 'Active' || p.accountStatus === 'Deactivated'
}

export function EditPersonDialog({ person, onClose }: { person: Person; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const mutation = useUpdatePersonMutation()
  const [formError, setFormError] = useState<string | null>(null)
  const locked = emailLocked(person)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<EditPersonInput, unknown, EditPersonValues>({
    resolver: zodResolver(editPersonSchema),
    defaultValues: {
      firstName: person.firstName,
      middleName: person.middleName ?? '',
      lastName: person.lastName,
      email: person.email,
      position: person.position ?? '',
    },
  })

  const onSubmit = handleSubmit((v) => {
    if (mutation.isPending) return
    setFormError(null)
    mutation.mutate(
      {
        id: person.id,
        body: { firstName: v.firstName, middleName: v.middleName ?? null, lastName: v.lastName, email: v.email, position: v.position ?? null },
      },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: 'Changes saved.' })
          onClose()
        },
        onError: (e) => {
          if (isApiError(e, 409) && (e.code === 'email_locked' || e.code === 'duplicate_email')) {
            setError('email', { type: 'server', message: messageForCode(e.code) })
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
      title="Edit person"
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={mutation.isPending}>
            Save changes
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
          <Field
            label="Email"
            error={errors.email?.message}
            hint={locked ? "Can't change after the login is activated." : 'Can change until they accept their invite.'}
          >
            {(p) => <Input {...p} {...register('email')} type="email" readOnly={locked} autoComplete="off" />}
          </Field>
          <Field label="Position" optional error={errors.position?.message} className="sm:col-span-2">
            {(p) => <Input {...p} {...register('position')} placeholder="e.g. Logistics" autoComplete="off" />}
          </Field>
        </div>
      </form>
    </Dialog>
  )
}
