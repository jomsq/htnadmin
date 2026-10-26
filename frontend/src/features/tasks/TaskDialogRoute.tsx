import { useId, useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Select } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import { formatEventDay } from '@/features/events/format'
import { useMoveTaskMutation, useTaskQuery } from './api'
import { TaskActivitySection } from './components/TaskActivitySection'
import { TaskAside } from './components/TaskAside'
import { TaskAttachmentsSection } from './components/TaskAttachmentsSection'
import { TaskChecklistSection } from './components/TaskChecklistSection'
import { STATUS_LABELS, TASK_STATUSES, type TaskDetail, type TaskStatus } from './schemas'

/** Task detail (canvas frame 08) as a deep-linkable dialog over the board. */
export function TaskDialogRoute() {
  const { eventId = '', taskId = '' } = useParams()
  const { search } = useLocation()
  const navigate = useNavigate()
  const query = useTaskQuery(taskId)

  const close = () => void navigate(`/events/${encodeURIComponent(eventId)}/tasks${search}`)

  if (query.isPending) {
    return (
      <Dialog open onClose={close} size="xl" title="Loading task…">
        <div role="status" aria-label="Loading the task" className="flex flex-col gap-3">
          <Skeleton className="h-6 w-72" />
          <Skeleton className="h-24 w-full" />
        </div>
      </Dialog>
    )
  }

  if (query.isError) {
    const notFound = isApiError(query.error, 404)
    return (
      <Dialog open onClose={close} size="md" title={notFound ? 'Task not found' : 'Could not load the task'}>
        <Alert
          tone="red"
          live="alert"
          action={
            notFound ? (
              <Button variant="secondary" size="sm" onClick={close}>
                Back to the board
              </Button>
            ) : (
              <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
                Try again
              </Button>
            )
          }
        >
          {notFound ? 'This task was archived or the link is wrong.' : errorMessage(query.error)}
        </Alert>
      </Dialog>
    )
  }

  return <TaskDialog task={query.data} onClose={close} />
}

function TaskDialog({ task, onClose }: { task: TaskDetail; onClose: () => void }) {
  const statusId = useId()
  const { toast } = useToast()
  const move = useMoveTaskMutation(task.eventId)
  const [moveError, setMoveError] = useState<string | null>(null)

  function onStatusChange(status: TaskStatus) {
    setMoveError(null)
    move.mutate(
      { taskId: task.id, status },
      {
        onSuccess: () => toast({ tone: 'success', title: `${task.reference} moved to ${STATUS_LABELS[status]}.` }),
        onError: (e) => setMoveError(errorMessage(e)),
      },
    )
  }

  const due = formatEventDay(task.dueAt)

  return (
    <Dialog
      open
      onClose={onClose}
      size="xl"
      titleSize="lg"
      bodyClassName="p-0"
      eyebrow={`${task.eventName} · ${task.committeeName} · ${task.reference}`}
      title={task.title}
    >
      <div className="flex flex-col lg:flex-row">
        <div className="flex min-w-0 flex-1 flex-col gap-6 p-5">
          <div className="flex flex-wrap items-center gap-2">
            <label htmlFor={statusId} className="text-[0.8125rem] font-medium text-gray-700 dark:text-gray-200">
              Status
            </label>
            <Select
              id={statusId}
              value={task.status}
              disabled={!task.canWork || move.isPending}
              onChange={(e) => onStatusChange(e.target.value as TaskStatus)}
              className="w-auto"
            >
              {TASK_STATUSES.map((s) => (
                <option key={s} value={s}>
                  {STATUS_LABELS[s]}
                </option>
              ))}
            </Select>
            {due ? <Badge color={task.status === 'Done' ? 'gray' : 'red'}>Due {due}</Badge> : null}
            {task.labels.map((label) => (
              <Badge key={label} color="gray">
                {label}
              </Badge>
            ))}
            {task.isArchived ? <Badge color="gray">Archived</Badge> : null}
          </div>

          {task.isArchived ? (
            <Alert tone="gray">
              This task is archived, so it is off the board and read only.
              {task.archiveReason?.trim() ? <> Reason: {task.archiveReason.trim()}</> : null}
            </Alert>
          ) : null}

          {moveError ? (
            <Alert tone="red" live="alert">
              {moveError}
            </Alert>
          ) : null}

          <section className="flex flex-col gap-2">
            <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-50">Description</h3>
            <p className="text-sm leading-relaxed whitespace-pre-line text-gray-700 dark:text-gray-300">
              {task.description?.trim() ? task.description : 'No description yet.'}
            </p>
          </section>

          <TaskChecklistSection task={task} />
          <TaskAttachmentsSection task={task} />
          <TaskActivitySection task={task} />
        </div>

        <TaskAside task={task} onArchived={onClose} />
      </div>
    </Dialog>
  )
}
