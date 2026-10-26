import { useId } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Textarea } from '@/components/ui/Input'
import { reasonSchema, type ReasonValues } from '../schemas'

/** Confirmation with a required reason (kept in the registration history and the audit log). */
export function ReasonDialog({
  title,
  description,
  confirmLabel,
  placeholder,
  pending,
  onConfirm,
  onClose,
}: {
  title: string
  description: string
  confirmLabel: string
  placeholder?: string
  pending: boolean
  onConfirm: (reason: string) => void
  onClose: () => void
}) {
  const formId = useId()
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ReasonValues>({ resolver: zodResolver(reasonSchema), defaultValues: { reason: '' } })

  const onSubmit = handleSubmit(({ reason }) => {
    if (!pending) onConfirm(reason)
  })

  return (
    <Dialog
      open
      onClose={onClose}
      role="alertdialog"
      size="sm"
      title={title}
      description={description}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Keep it
          </Button>
          <Button type="submit" form={formId} variant="destructive" loading={pending}>
            {confirmLabel}
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit}>
        <Field label="Reason" error={errors.reason?.message}>
          {(p) => <Textarea {...p} {...register('reason')} rows={3} maxLength={500} placeholder={placeholder} />}
        </Field>
      </form>
    </Dialog>
  )
}
