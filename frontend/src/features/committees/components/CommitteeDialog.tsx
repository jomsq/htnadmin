import { useId, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useCreateCommitteeMutation, useRenameCommitteeMutation } from '../api'
import { committeeFormSchema, type Committee, type CommitteeBody, type CommitteeFormInput } from '../schemas'

/** New committee, or rename an existing one. Duplicate names come back on the name field. */
export function CommitteeDialog({ eventId, committee, onClose }: { eventId: string; committee: Committee | null; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const create = useCreateCommitteeMutation(eventId)
  const rename = useRenameCommitteeMutation(eventId)
  const pending = create.isPending || rename.isPending
  const [formError, setFormError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<CommitteeFormInput, unknown, CommitteeBody>({
    resolver: zodResolver(committeeFormSchema),
    defaultValues: { name: committee?.name ?? '' },
  })

  const onSubmit = handleSubmit((body) => {
    if (pending) return
    setFormError(null)
    const onError = (e: unknown) => {
      if (isApiError(e) && (e.code === 'duplicate_name' || e.fieldErrors.name)) {
        setError('name', { type: 'server', message: e.fieldErrors.name ?? errorMessage(e) })
        return
      }
      setFormError(errorMessage(e))
    }
    const onSuccess = () => {
      toast({ tone: 'success', title: committee ? `${body.name} saved.` : `${body.name} added.` })
      onClose()
    }
    if (committee) rename.mutate({ committeeId: committee.id, body }, { onSuccess, onError })
    else create.mutate(body, { onSuccess, onError })
  })

  return (
    <Dialog
      open
      onClose={onClose}
      size="sm"
      title={committee ? `Rename ${committee.name}` : 'New committee'}
      description={committee ? 'The task board and history keep the same committee.' : 'Pick a goon as lead after you create it.'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={pending}>
            {committee ? 'Save committee' : 'Create committee'}
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
        <Field label="Committee name" error={errors.name?.message} hint="For example Logistics, Programs or Tech &amp; AV.">
          {(p) => <Input {...p} {...register('name')} autoComplete="off" maxLength={60} />}
        </Field>
      </form>
    </Dialog>
  )
}
