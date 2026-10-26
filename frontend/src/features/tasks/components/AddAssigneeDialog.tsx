import { useEffect, useId, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Select } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useCommitteesQuery } from '@/features/committees/api'
import { personName } from '@/features/committees/schemas'
import { useAddAssigneeMutation } from '../api'
import {
  TASK_ROLES,
  assigneeFormSchema,
  formatMultiplier,
  formatPoints,
  multiplierFor,
  projectedPoints,
  type AssigneeFormInput,
  type TaskDetail,
  type TaskRole,
} from '../schemas'

type Choice = { value: string; label: string; isVolunteer: boolean }

/** People and volunteers on this event's committees, minus whoever is already on the task. */
function useChoices(task: TaskDetail): Choice[] {
  const committees = useCommitteesQuery(task.eventId)
  const taken = new Set([...task.assignees.map((a) => a.personId ?? ''), ...task.assignees.map((a) => a.volunteerId ?? '')])
  const out: Choice[] = []
  for (const c of committees.data?.committees ?? []) {
    for (const p of [c.lead, ...c.members]) {
      if (!p || taken.has(p.personId)) continue
      if (out.some((o) => o.value === `person:${p.personId}`)) continue
      out.push({ value: `person:${p.personId}`, label: `${personName(p)} · ${c.name}`, isVolunteer: false })
    }
    for (const v of c.volunteers) {
      if (taken.has(v.id) || out.some((o) => o.value === `volunteer:${v.id}`)) continue
      out.push({ value: `volunteer:${v.id}`, label: `${v.name} · Volunteer`, isVolunteer: true })
    }
  }
  return out
}

/** Add a lead, collab or assistant (canvas frame 08). Volunteers are always Assistant and earn 0. */
export function AddAssigneeDialog({ task, basePoints, onClose }: { task: TaskDetail; basePoints: number; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const add = useAddAssigneeMutation(task.id)
  const choices = useChoices(task)
  const [formError, setFormError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    control,
    setValue,
    formState: { errors },
  } = useForm<AssigneeFormInput>({
    resolver: zodResolver(assigneeFormSchema),
    defaultValues: { who: '', role: 'Collab' },
  })

  const who = useWatch({ control, name: 'who' })
  const role = (useWatch({ control, name: 'role' }) ?? 'Collab') as TaskRole
  const isVolunteer = choices.find((c) => c.value === who)?.isVolunteer ?? false

  // A volunteer helps without earning points, so the role is forced to Assistant.
  useEffect(() => {
    if (isVolunteer && role !== 'Assistant') setValue('role', 'Assistant')
  }, [isVolunteer, role, setValue])

  const multiplier = multiplierFor(role, task.multipliers)
  const points = projectedPoints(basePoints, multiplier, isVolunteer)

  const onSubmit = handleSubmit((values) => {
    if (add.isPending) return
    setFormError(null)
    const [kind, id] = values.who.split(':')
    add.mutate(
      kind === 'volunteer' ? { volunteerId: id, role: 'Assistant' } : { personId: id, role: values.role },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: 'Assignee added.' })
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
      title="Add lead, collab or assistant"
      description="Points are awarded when the task is approved into Done."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={add.isPending}>
            Add to task
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
        <Field label="Person or volunteer" error={errors.who?.message}>
          {(p) => (
            <Select {...p} {...register('who')}>
              <option value="">Pick someone…</option>
              {choices.map((c) => (
                <option key={c.value} value={c.value}>
                  {c.label}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Role" error={errors.role?.message} hint={isVolunteer ? 'Volunteers help as Assistant and never earn points.' : undefined}>
          {(p) => (
            <Select {...p} {...register('role')} disabled={isVolunteer}>
              {TASK_ROLES.map((r) => (
                <option key={r} value={r}>
                  {r} · ×{formatMultiplier(multiplierFor(r, task.multipliers))}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <p className="flex items-center gap-2 text-[0.8125rem] text-gray-600 dark:text-gray-400">
          On completion: <strong className="font-semibold text-gray-900 tabular-nums dark:text-gray-50">{formatPoints(points)} pts</strong>
          {isVolunteer ? <Badge color="yellow">Volunteer</Badge> : null}
        </p>
      </form>
    </Dialog>
  )
}
