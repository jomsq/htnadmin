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
import { useReopenTaskMutation } from '../api'
import { reasonFormSchema, type ReasonFormInput, type TaskDetail } from '../schemas'

/** Reopening a Done task reverses its awards, so a reason is required and kept in the log. */
export function ReopenDialog({ task, onClose }: { task: TaskDetail; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const reopen = useReopenTaskMutation(task.id)
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ReasonFormInput>({ resolver: zodResolver(reasonFormSchema), defaultValues: { reason: '' } })

  const onSubmit = handleSubmit(({ reason }) => {
    if (reopen.isPending) return
    setFormError(null)
    reopen.mutate(
      { reason: reason.trim(), status: 'Review' },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: `${task.reference} is back in Review.`, description: 'The awarded points were reversed.' })
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
      size="sm"
      role="alertdialog"
      title={`Reopen ${task.reference}?`}
      description="The awarded points are reversed with an offsetting entry. Nothing is deleted."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="destructive" loading={reopen.isPending}>
            Reopen task
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
        <Field label="Reason" error={errors.reason?.message} hint="Everyone on the task sees this in the activity log.">
          {(p) => <Textarea {...p} rows={3} maxLength={500} {...register('reason')} />}
        </Field>
      </form>
    </Dialog>
  )
}
