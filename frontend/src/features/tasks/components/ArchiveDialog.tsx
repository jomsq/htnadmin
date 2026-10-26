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
import { useArchiveTaskMutation } from '../api'
import { reasonFormSchema, type ReasonFormInput, type TaskDetail } from '../schemas'

/**
 * Archiving takes a card off the board (nothing is deleted), so a reason is required and kept in the log.
 * A task that still holds live awards is refused with `reopen_first`: the points would stay on the
 * leaderboard while the card left the board.
 */
export function ArchiveDialog({ task, onClose, onArchived }: { task: TaskDetail; onClose: () => void; onArchived?: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const archive = useArchiveTaskMutation(task.id)
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ReasonFormInput>({ resolver: zodResolver(reasonFormSchema), defaultValues: { reason: '' } })

  const onSubmit = handleSubmit(({ reason }) => {
    if (archive.isPending) return
    setFormError(null)
    archive.mutate(
      { reason: reason.trim() },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: `${task.reference} was archived.`, description: 'It can be restored from the board filters.' })
          onClose()
          onArchived?.()
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
      title={`Archive ${task.reference}?`}
      description="The card leaves the board. Nothing is deleted and it can be restored."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="destructive" loading={archive.isPending}>
            Archive task
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
