import { useEffect, useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Stepper } from '@/components/ui/Stepper'
import { CloseIcon, LockIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { formatEventDay } from '@/features/events/format'
import { useApproveTaskMutation, useRemoveAssigneeMutation, useUpdateTaskMutation } from '../api'
import { formatMultiplier, formatPoints, projectedPoints, type TaskDetail } from '../schemas'
import { AddAssigneeDialog } from './AddAssigneeDialog'
import { ReopenDialog } from './ReopenDialog'
import { RoleAvatar } from './RoleAvatar'

/** Right column of the task dialog (canvas frame 08): base points, the points split and approval. */
export function TaskAside({ task }: { task: TaskDetail }) {
  const { toast } = useToast()
  const update = useUpdateTaskMutation(task.id)
  const approve = useApproveTaskMutation(task.id)
  const removeAssignee = useRemoveAssigneeMutation(task.id)
  // Local draft of the base points; null means "the saved value".
  const [draft, setDraft] = useState<number | null>(null)
  const base = draft ?? task.basePoints
  const [addOpen, setAddOpen] = useState(false)
  const [reopenOpen, setReopenOpen] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  // Saved a moment after the last click, so a run of − / + clicks is one request.
  useEffect(() => {
    if (draft === null || draft === task.basePoints) return
    const timer = window.setTimeout(() => {
      update.mutate(
        {
          title: task.title,
          description: task.description ?? undefined,
          dueAt: task.dueAt ?? null,
          labels: task.labels,
          basePoints: draft,
          committeeId: task.committeeId,
        },
        {
          onSuccess: () => setDraft(null),
          onError: (e) => {
            setDraft(null)
            toast({ tone: 'error', title: 'Could not change the base points.', description: errorMessage(e) })
          },
        },
      )
    }, 600)
    return () => window.clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft])

  function onApprove() {
    if (approve.isPending) return
    setActionError(null)
    approve.mutate(undefined, {
      onSuccess: () => toast({ tone: 'success', title: `${task.reference} is done.`, description: 'Points were awarded to the assignees.' }),
      onError: (e) => setActionError(errorMessage(e)),
    })
  }

  const presets = [
    ['Small', task.presets.small],
    ['Medium', task.presets.medium],
    ['Large', task.presets.large],
    ['XL', task.presets.xl],
  ] as const

  return (
    <aside
      aria-label="Points and approval"
      className="flex w-full flex-col gap-6 border-t border-gray-200 bg-gray-50 p-5 lg:w-80 lg:shrink-0 lg:border-t-0 lg:border-l dark:border-white/10 dark:bg-gray-950/40"
    >
      {task.canEdit ? (
        <section className="flex flex-col gap-2.5">
          <div className="flex items-center justify-between gap-2">
            <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-50">Base points</h3>
            <span className="inline-flex items-center gap-1 text-xs text-gray-600 dark:text-gray-400">
              <LockIcon size={12} />
              Admin &amp; lead only
            </span>
          </div>
          <Stepper name="base points" value={base} onChange={setDraft} min={0} max={1000} step={5} />
          <div className="flex flex-wrap gap-2">
            {presets.map(([label, value]) => (
              <Button key={label} size="sm" variant={base === value ? 'primary' : 'secondary'} onClick={() => setDraft(value)}>
                {label} · {value}
              </Button>
            ))}
          </div>
        </section>
      ) : null}

      <section className="flex flex-col gap-2.5">
        <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-50">Assignees &amp; points on completion</h3>
        {task.assignees.length === 0 ? (
          <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
            Nobody is on this task yet. A task needs a Lead before it can be approved.
          </p>
        ) : null}
        <ul className="flex flex-col gap-2">
          {task.assignees.map((a) => (
            <li
              key={a.id}
              className="flex items-center gap-2.5 rounded-md bg-white px-3 py-2.5 shadow-sm ring-1 ring-gray-900/8 dark:bg-gray-900 dark:ring-white/10"
            >
              <RoleAvatar initials={a.initials} name={a.name} role={a.role} isVolunteer={a.isVolunteer} size="md" />
              <div className="flex min-w-0 flex-1 flex-col">
                <span className="flex items-center gap-1.5 truncate text-sm font-medium text-gray-900 dark:text-gray-50">
                  {a.name}
                  {a.isVolunteer ? <Badge color="yellow">Volunteer</Badge> : null}
                </span>
                <span className="text-xs text-gray-600 dark:text-gray-400">
                  {a.role} · × {formatMultiplier(a.multiplier)}
                </span>
              </div>
              <span className="text-[0.9375rem] font-semibold text-gray-900 tabular-nums dark:text-gray-50">
                {formatPoints(projectedPoints(base, a.multiplier, a.isVolunteer))} pts
              </span>
              {task.canEdit ? (
                <button
                  type="button"
                  aria-label={`Remove ${a.name} from the task`}
                  disabled={removeAssignee.isPending}
                  onClick={() =>
                    removeAssignee.mutate(
                      { assigneeId: a.id },
                      { onError: (e) => toast({ tone: 'error', title: `Could not remove ${a.name}.`, description: errorMessage(e) }) },
                    )
                  }
                  className="inline-flex size-9 shrink-0 items-center justify-center rounded-md text-gray-600 hover:bg-gray-100 hover:text-red-700 disabled:opacity-40 dark:text-gray-400 dark:hover:bg-white/5"
                >
                  <CloseIcon size={16} />
                </button>
              ) : null}
            </li>
          ))}
        </ul>
        {task.canEdit ? (
          <Button variant="dashed" onClick={() => setAddOpen(true)}>
            + Add lead, collab or assistant
          </Button>
        ) : null}
        <p className="text-xs leading-relaxed text-gray-600 dark:text-gray-400">
          Points are awarded when the task moves to Done. Multipliers come from Settings → Points.
        </p>
      </section>

      <section className="flex flex-col gap-2 text-[0.8125rem]">
        <div className="flex justify-between gap-3">
          <span className="text-gray-600 dark:text-gray-400">Committee</span>
          <span className="font-medium text-gray-900 dark:text-gray-50">{task.committeeName}</span>
        </div>
        <div className="flex justify-between gap-3">
          <span className="text-gray-600 dark:text-gray-400">Assigned by</span>
          <span className="font-medium text-gray-900 dark:text-gray-50">{task.assignedByName ?? task.createdByName ?? '—'}</span>
        </div>
        <div className="flex justify-between gap-3">
          <span className="text-gray-600 dark:text-gray-400">Created</span>
          <span className="font-medium text-gray-900 dark:text-gray-50">{formatEventDay(task.createdAt)}</span>
        </div>
      </section>

      {actionError ? (
        <Alert tone="red" live="alert">
          {actionError}
        </Alert>
      ) : null}

      {task.status === 'Done' ? (
        task.canApprove ? (
          <Button variant="danger" size="lg" onClick={() => setReopenOpen(true)}>
            Reopen
          </Button>
        ) : null
      ) : task.canApprove && task.status === 'Review' ? (
        <Button variant="success" size="lg" onClick={onApprove} loading={approve.isPending}>
          Approve &amp; mark done
        </Button>
      ) : task.requireTaskApproval && !task.canApprove ? (
        <Alert tone="gray">Only the committee lead or an admin can finish a task.</Alert>
      ) : null}

      {addOpen ? <AddAssigneeDialog task={task} basePoints={base} onClose={() => setAddOpen(false)} /> : null}
      {reopenOpen ? <ReopenDialog task={task} onClose={() => setReopenOpen(false)} /> : null}
    </aside>
  )
}
