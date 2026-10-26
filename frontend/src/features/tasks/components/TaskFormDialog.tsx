import { useId, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { fromManilaInput } from '@/features/events/format'
import { useOrgSettingsQuery } from '@/features/settings/api'
import { defaultPoints } from '@/features/settings/schemas'
import { useCreateTaskMutation } from '../api'
import {
  STATUS_LABELS,
  TASK_STATUSES,
  taskFormSchema,
  type BoardCommittee,
  type TaskFormInput,
  type TaskFormValues,
  type TaskStatus,
} from '../schemas'

type Props = {
  eventId: string
  committees: BoardCommittee[]
  status: TaskStatus
  committeeId?: string
  onClose: () => void
}

/** "+ New task" / "+ Add task": a task always belongs to one committee the user may manage. */
export function TaskFormDialog({ eventId, committees, status, committeeId, onClose }: Props) {
  const formId = useId()
  const { toast } = useToast()
  const navigate = useNavigate()
  const create = useCreateTaskMutation(eventId)
  const settings = useOrgSettingsQuery()
  const presets = settings.data?.points.presets ?? defaultPoints.presets
  const [formError, setFormError] = useState<string | null>(null)

  const manageable = committees.filter((c) => c.canManage)
  const {
    register,
    handleSubmit,
    setValue,
    control,
    formState: { errors },
  } = useForm<TaskFormInput, unknown, TaskFormValues>({
    resolver: zodResolver(taskFormSchema),
    defaultValues: {
      committeeId: committeeId ?? manageable[0]?.id ?? '',
      title: '',
      description: '',
      dueAt: '',
      basePoints: presets.medium,
      status,
    },
  })
  const basePoints = useWatch({ control, name: 'basePoints' })

  const onSubmit = handleSubmit((values) => {
    if (create.isPending) return
    setFormError(null)
    create.mutate(
      {
        committeeId: values.committeeId,
        title: values.title,
        description: values.description,
        status: values.status,
        basePoints: values.basePoints,
        dueAt: values.dueAt ? fromManilaInput(values.dueAt) : undefined,
      },
      {
        onSuccess: (detail) => {
          toast({ tone: 'success', title: `${detail.reference} created.`, description: 'Add a lead and collaborators so points can be awarded.' })
          onClose()
          void navigate(`/events/${encodeURIComponent(eventId)}/tasks/${encodeURIComponent(detail.id)}`)
        },
        onError: (e) => setFormError(errorMessage(e)),
      },
    )
  })

  return (
    <Dialog
      open
      onClose={onClose}
      title="New task"
      description={`It starts in ${STATUS_LABELS[status]}. Points are awarded when the task is approved into Done.`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={create.isPending}>
            Create task
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
        <Field label="Title" error={errors.title?.message}>
          {(p) => <Input {...p} {...register('title')} autoComplete="off" maxLength={140} />}
        </Field>
        <Field label="Committee" error={errors.committeeId?.message}>
          {(p) => (
            <Select {...p} {...register('committeeId')}>
              {manageable.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Status" error={errors.status?.message}>
          {(p) => (
            <Select {...p} {...register('status')}>
              {TASK_STATUSES.map((s) => (
                <option key={s} value={s}>
                  {STATUS_LABELS[s]}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Due" optional error={errors.dueAt?.message} hint="Asia/Manila time.">
          {(p) => <Input {...p} type="datetime-local" {...register('dueAt')} />}
        </Field>
        <Field label="Base points" error={errors.basePoints?.message} hint="Each assignee earns base points × their role multiplier.">
          {(p) => (
            <div className="flex flex-col gap-2">
              <Input {...p} type="number" min={0} max={1000} step={5} className="w-28 tabular-nums" {...register('basePoints')} />
              <div className="flex flex-wrap gap-2">
                {([
                  ['Small', presets.small],
                  ['Medium', presets.medium],
                  ['Large', presets.large],
                  ['XL', presets.xl],
                ] as const).map(([label, value]) => (
                  <Button
                    key={label}
                    variant={Number(basePoints) === value ? 'primary' : 'secondary'}
                    size="sm"
                    onClick={() => setValue('basePoints', value, { shouldValidate: true })}
                  >
                    {label} · {value}
                  </Button>
                ))}
              </div>
            </div>
          )}
        </Field>
        <Field label="Description" optional error={errors.description?.message}>
          {(p) => <Textarea {...p} rows={4} {...register('description')} />}
        </Field>
      </form>
    </Dialog>
  )
}
