import { useId, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Textarea } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useDeactivateCommitteeMutation } from '../api'
import { reasonFormSchema, type Committee, type ReasonBody, type ReasonFormInput } from '../schemas'

/** Committees are never deleted. Deactivating needs a reason and fails while tasks are open. */
export function DeactivateCommitteeDialog({ eventId, committee, onClose }: { eventId: string; committee: Committee; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const deactivate = useDeactivateCommitteeMutation(eventId)
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ReasonFormInput, unknown, ReasonBody>({ resolver: zodResolver(reasonFormSchema), defaultValues: { reason: '' } })

  const onSubmit = handleSubmit(({ reason }) => {
    if (deactivate.isPending) return
    setFormError(null)
    deactivate.mutate(
      { committeeId: committee.id, reason },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: `${committee.name} is deactivated.`, description: 'Its history and past tasks are kept.' })
          onClose()
        },
        onError: (e) => setFormError(errorMessage(e)),
      },
    )
  })

  return (
    <Dialog
      open
      onClose={onClose}
      role="alertdialog"
      size="sm"
      title={`Deactivate ${committee.name}?`}
      description="Nothing is deleted. Members leave the committee and it stops taking new tasks."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Keep it
          </Button>
          <Button type="submit" form={formId} variant="destructive" loading={deactivate.isPending}>
            Deactivate committee
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
        <Field label="Reason" error={errors.reason?.message} hint="Kept in the audit log.">
          {(p) => <Textarea {...p} {...register('reason')} rows={3} maxLength={500} />}
        </Field>
      </form>
    </Dialog>
  )
}
