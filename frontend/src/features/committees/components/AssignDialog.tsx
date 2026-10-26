import { useId, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Select } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useAddMemberMutation, useAttachVolunteerMutation } from '../api'
import { assignFormSchema, type AssignFormInput, type Committee } from '../schemas'

export type AssignTarget = { kind: 'person'; id: string; name: string } | { kind: 'volunteer'; id: string; name: string }

/** Assign someone from the unassigned pool: pick one of the committees you manage. */
export function AssignDialog({
  eventId,
  target,
  committees,
  onClose,
}: {
  eventId: string
  target: AssignTarget
  committees: Committee[]
  onClose: () => void
}) {
  const formId = useId()
  const { toast } = useToast()
  const addMember = useAddMemberMutation(eventId)
  const attach = useAttachVolunteerMutation(eventId)
  const pending = addMember.isPending || attach.isPending
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<AssignFormInput>({
    resolver: zodResolver(assignFormSchema),
    defaultValues: { committeeId: committees.length === 1 ? committees[0].id : '' },
  })

  const onSubmit = handleSubmit(({ committeeId }) => {
    if (pending) return
    setFormError(null)
    const name = committees.find((c) => c.id === committeeId)?.name ?? 'the committee'
    const handlers = {
      onSuccess: () => {
        toast({ tone: 'success', title: `${target.name} is on ${name}.` })
        onClose()
      },
      onError: (e: unknown) => setFormError(errorMessage(e)),
    }
    if (target.kind === 'person') addMember.mutate({ committeeId, personId: target.id }, handlers)
    else attach.mutate({ committeeId, volunteerId: target.id }, handlers)
  })

  return (
    <Dialog
      open
      onClose={onClose}
      size="sm"
      title={`Assign ${target.name}`}
      description="One committee per person per event."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={pending}>
            Assign
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
        <Field label="Committee" error={errors.committeeId?.message}>
          {(p) => (
            <Select {...p} {...register('committeeId')}>
              <option value="">Pick a committee</option>
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
