import { useId, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Select } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useCreateVolunteerMutation } from '../api'
import { volunteerFormSchema, type Committee, type VolunteerBody, type VolunteerFormInput } from '../schemas'

const fieldNames = ['name', 'email', 'mobile', 'committeeId'] as const
type FieldName = (typeof fieldNames)[number]
const isFieldName = (k: string): k is FieldName => (fieldNames as readonly string[]).includes(k)

/** A volunteer helps at one event: no login, no points, name required and contact optional. */
export function VolunteerDialog({ eventId, committees, onClose }: { eventId: string; committees: Committee[]; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const create = useCreateVolunteerMutation(eventId)
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<VolunteerFormInput, unknown, VolunteerBody>({
    resolver: zodResolver(volunteerFormSchema),
    defaultValues: { name: '', email: '', mobile: '', committeeId: '' },
  })

  const onSubmit = handleSubmit((body) => {
    if (create.isPending) return
    setFormError(null)
    create.mutate(body, {
      onSuccess: () => {
        toast({ tone: 'success', title: `${body.name} is added as a volunteer.` })
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
    })
  })

  return (
    <Dialog
      open
      onClose={onClose}
      title="Add volunteer (no login)"
      description="Volunteers help at this event only. They get no account and earn no points."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={create.isPending}>
            Add volunteer
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
        <Field label="Name" error={errors.name?.message}>
          {(p) => <Input {...p} {...register('name')} autoComplete="off" maxLength={120} />}
        </Field>
        <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">
          <Field label="Email" optional error={errors.email?.message}>
            {(p) => <Input {...p} {...register('email')} type="email" autoComplete="off" />}
          </Field>
          <Field label="Mobile" optional error={errors.mobile?.message}>
            {(p) => <Input {...p} {...register('mobile')} inputMode="tel" autoComplete="off" />}
          </Field>
        </div>
        <Field label="Committee" optional error={errors.committeeId?.message} hint="You can assign them later.">
          {(p) => (
            <Select {...p} {...register('committeeId')}>
              <option value="">Not yet assigned</option>
              {committees.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
          )}
        </Field>
      </form>
    </Dialog>
  )
}
