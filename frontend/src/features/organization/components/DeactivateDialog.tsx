import { useId } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Textarea } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useDeactivatePersonMutation } from '../api'
import { fullName } from '../format'
import { deactivateSchema, type DeactivateValues, type Person } from '../schemas'

/** Confirm deactivation with a reason (kept in the person's history and the audit log). */
export function DeactivateDialog({ person, onClose, onDone }: { person: Person; onClose: () => void; onDone: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const mutation = useDeactivatePersonMutation()
  const name = fullName(person)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<DeactivateValues>({ resolver: zodResolver(deactivateSchema), defaultValues: { reason: '' } })

  const onSubmit = handleSubmit(({ reason }) => {
    if (mutation.isPending) return
    mutation.mutate(
      { id: person.id, reason },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: `${name} was deactivated.` })
          onDone()
        },
        onError: (e) => {
          toast({ tone: 'error', title: 'Could not deactivate this account.', description: errorMessage(e) })
          onClose()
        },
      },
    )
  })

  return (
    <Dialog
      open
      onClose={onClose}
      role="alertdialog"
      size="sm"
      title={`Deactivate ${name}?`}
      description="They will be signed out and can no longer sign in. Their record and history are kept."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="destructive" loading={mutation.isPending}>
            Deactivate account
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit}>
        <Field label="Reason" error={errors.reason?.message}>
          {(p) => <Textarea {...p} {...register('reason')} rows={3} placeholder="e.g. Stepped down after HTN Con 2026" />}
        </Field>
      </form>
    </Dialog>
  )
}
