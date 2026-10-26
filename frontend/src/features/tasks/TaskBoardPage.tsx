import { useId, useState } from 'react'
import { Outlet, useParams, useSearchParams } from 'react-router'
import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCorners,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import { sortableKeyboardCoordinates } from '@dnd-kit/sortable'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { FilterChip } from '@/components/ui/FilterChip'
import { Skeleton } from '@/components/ui/Spinner'
import { Switch } from '@/components/ui/Switch'
import { CheckSquareIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { useOrgSettingsQuery } from '@/features/settings/api'
import { defaultPoints } from '@/features/settings/schemas'
import { useMoveTaskMutation, useTaskBoardQuery } from './api'
import { BoardColumnPanel } from './components/BoardColumn'
import { TaskFormDialog } from './components/TaskFormDialog'
import { STATUS_LABELS, TASK_STATUSES, formatMultiplier, type TaskStatus } from './schemas'

/** Task board (canvas frame 07). The task dialog is a child route, so it is deep-linkable. */
export function TaskBoardPage() {
  const { eventId = '' } = useParams()
  const [search, setSearch] = useSearchParams()
  const committeeId = search.get('committee') ?? undefined
  const mine = search.get('mine') === '1'
  const mineLabelId = useId()
  const { toast } = useToast()
  const [newTask, setNewTask] = useState<TaskStatus | null>(null)
  const [moveError, setMoveError] = useState<string | null>(null)

  const query = useTaskBoardQuery(eventId, { committeeId, mine })
  const move = useMoveTaskMutation(eventId)
  const settings = useOrgSettingsQuery()
  const points = settings.data?.points ?? defaultPoints

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )

  function setParam(key: string, value: string | null) {
    const next = new URLSearchParams(search)
    if (value) next.set(key, value)
    else next.delete(key)
    setSearch(next, { replace: true })
  }

  function moveTask(taskId: string, status: TaskStatus) {
    setMoveError(null)
    move.mutate(
      { taskId, status },
      {
        onSuccess: (detail) => toast({ tone: 'success', title: `${detail.reference} moved to ${STATUS_LABELS[status]}.` }),
        onError: (e) => {
          const message = errorMessage(e)
          setMoveError(message)
          toast({ tone: 'error', title: 'Could not move the task.', description: message })
        },
      },
    )
  }

  function onDragEnd(event: DragEndEvent) {
    // Both the columns (droppables) and the cards (sortables) carry their status in `data`.
    const from = event.active.data.current?.status as TaskStatus | undefined
    const to = (event.over?.data.current as { status?: TaskStatus } | undefined)?.status
    if (!from || !to || from === to) return
    moveTask(String(event.active.id), to)
  }

  if (query.isPending) {
    return (
      <div role="status" aria-label="Loading the task board" className="flex gap-3 overflow-hidden">
        {TASK_STATUSES.map((s) => (
          <Skeleton key={s} className="h-64 w-72 shrink-0" />
        ))}
      </div>
    )
  }

  if (query.isError) {
    return (
      <Alert
        tone="red"
        live="alert"
        action={
          <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
            Try again
          </Button>
        }
      >
        Could not load the task board. {errorMessage(query.error)}
      </Alert>
    )
  }

  const board = query.data
  const total = board.columns.reduce((sum, c) => sum + c.count, 0)

  return (
    <>
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-1">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-50">Task board</h2>
          <p className="text-sm text-gray-600 dark:text-gray-400">
            Points are awarded when a task is approved into Done. Only the committee lead or an admin can approve.
          </p>
        </div>
        {board.canCreateTask ? <Button onClick={() => setNewTask('Backlog')}>+ New task</Button> : null}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <FilterChip selected={!committeeId} onClick={() => setParam('committee', null)}>
          All
        </FilterChip>
        {board.committees.map((c) => (
          <FilterChip key={c.id} selected={committeeId === c.id} onClick={() => setParam('committee', c.id)}>
            {c.name}
          </FilterChip>
        ))}
        <span className="ml-auto flex items-center gap-2">
          <span id={mineLabelId} className="text-[0.8125rem] font-semibold text-gray-700 dark:text-gray-200">
            Only my tasks
          </span>
          <Switch checked={mine} aria-labelledby={mineLabelId} onChange={(on) => setParam('mine', on ? '1' : null)} />
        </span>
      </div>

      {moveError ? (
        <Alert tone="red" live="alert">
          {moveError}
        </Alert>
      ) : null}

      {total === 0 ? (
        <Card>
          <EmptyState
            icon={<CheckSquareIcon size={22} />}
            title={mine || committeeId ? 'No tasks match this filter' : 'No tasks yet'}
            description={
              mine || committeeId
                ? 'Clear the filters to see the whole board.'
                : 'Add the first task for a committee. Each task carries base points and a checklist.'
            }
            action={
              board.canCreateTask ? (
                <Button onClick={() => setNewTask('Backlog')}>+ New task</Button>
              ) : undefined
            }
          />
        </Card>
      ) : (
        <DndContext sensors={sensors} collisionDetection={closestCorners} onDragEnd={onDragEnd}>
          <div className="overflow-x-auto pb-2">
            <div className="flex items-start gap-3">
              {board.columns.map((column) => (
                <BoardColumnPanel
                  key={column.status}
                  column={column}
                  eventId={eventId}
                  canCreateTask={board.canCreateTask}
                  onAddTask={(status) => setNewTask(status)}
                  onMove={moveTask}
                />
              ))}
            </div>
          </div>
        </DndContext>
      )}

      <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
        Avatar rings: blue = Lead (×{formatMultiplier(points.leadMultiplier)}), teal = Collab (×{formatMultiplier(points.collabMultiplier)}), amber =
        Assistant (×{formatMultiplier(points.assistantMultiplier)}). Moving a card to Done awards points once the lead or an admin approves it in
        Review.
      </p>

      {newTask ? (
        <TaskFormDialog
          eventId={eventId}
          committees={board.committees}
          status={newTask}
          committeeId={committeeId}
          onClose={() => setNewTask(null)}
        />
      ) : null}

      <Outlet />
    </>
  )
}
