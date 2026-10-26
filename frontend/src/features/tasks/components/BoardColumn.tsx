import { useDroppable } from '@dnd-kit/core'
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { useLocation } from 'react-router'
import { cn } from '@/lib/cn'
import { STATUS_DOTS, STATUS_LABELS, type BoardColumn as Column, type TaskStatus } from '../schemas'
import { TaskCardTile } from './TaskCard'

type Props = {
  column: Column
  eventId: string
  canCreateTask: boolean
  onAddTask: (status: TaskStatus) => void
  onMove: (taskId: string, status: TaskStatus) => void
}

/** One kanban column (canvas frame 07): colour dot, name, count, cards and "+ Add task". */
export function BoardColumnPanel({ column, eventId, canCreateTask, onAddTask, onMove }: Props) {
  const label = STATUS_LABELS[column.status]
  // Keep the board filters in the URL while the dialog is open.
  const { search } = useLocation()
  const { setNodeRef, isOver } = useDroppable({ id: column.status, data: { status: column.status } })

  return (
    <section
      ref={setNodeRef}
      aria-label={label}
      className={cn(
        'flex w-72 shrink-0 flex-col gap-2.5 rounded-lg bg-gray-100 p-3 dark:bg-white/5',
        isOver && 'ring-2 ring-brand-600 dark:ring-brand-300',
      )}
    >
      <div className="flex items-center gap-2 px-1 py-0.5">
        <span aria-hidden="true" className={cn('size-2 shrink-0 rounded-full', STATUS_DOTS[column.status])} />
        <h3 className="flex-1 text-[0.8125rem] font-semibold text-gray-900 dark:text-gray-50">{label}</h3>
        <span className="text-xs font-semibold text-gray-600 tabular-nums dark:text-gray-400">
          {column.count}
          <span className="sr-only"> tasks</span>
        </span>
      </div>

      <ul className="flex flex-col gap-2.5">
        <SortableContext items={column.cards.map((c) => c.id)} strategy={verticalListSortingStrategy}>
          {column.cards.map((card) => (
            <TaskCardTile
              key={card.id}
              card={card}
              status={column.status}
              href={`/events/${encodeURIComponent(eventId)}/tasks/${encodeURIComponent(card.id)}${search}`}
              onMove={(status) => onMove(card.id, status)}
            />
          ))}
        </SortableContext>
      </ul>

      {column.cards.length === 0 ? <p className="px-1 text-xs text-gray-600 dark:text-gray-400">No tasks here yet.</p> : null}

      {/* Done is capped at the newest 50 cards, so the column says how many it really holds. */}
      {column.hasMore ? (
        <p className="px-1 text-xs text-gray-600 dark:text-gray-400">
          Showing the newest {column.cards.length} of {column.count} tasks.
        </p>
      ) : null}

      {canCreateTask ? (
        <button
          type="button"
          onClick={() => onAddTask(column.status)}
          className="flex min-h-9 items-center gap-1 rounded-md px-2 text-[0.8125rem] font-medium text-gray-600 hover:bg-gray-200/70 hover:text-gray-900 dark:text-gray-400 dark:hover:bg-white/5 dark:hover:text-gray-50"
        >
          + Add task
          <span className="sr-only"> to {label}</span>
        </button>
      ) : null}
    </section>
  )
}
