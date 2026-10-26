import { useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { Badge } from '@/components/ui/Badge'
import { Menu } from '@/components/ui/Menu'
import { CheckSquareIcon } from '@/components/ui/icons'
import { formatShortDay } from '@/features/events/format'
import { cn } from '@/lib/cn'
import { STATUS_LABELS, TASK_STATUSES, formatPoints, type TaskCard as Card, type TaskStatus } from '../schemas'
import { RoleAvatar } from './RoleAvatar'

export function GripIcon() {
  return (
    <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round">
      <path d="M9 6h.01M9 12h.01M9 18h.01M15 6h.01M15 12h.01M15 18h.01" />
    </svg>
  )
}

export const cardClass = 'relative flex flex-col gap-2.5 rounded-md bg-white p-3 shadow-sm ring-1 ring-gray-900/8 dark:bg-gray-900 dark:ring-white/10'

type ContentProps = {
  card: Card
  status: TaskStatus
  /** Link target; the dialog opens over the board. */
  href: string
  onMove?: (status: TaskStatus) => void
  /** Drag handle rendered next to Move to… (board only). */
  handle?: ReactNode
  /** Shown on /my-tasks, where cards from several events sit together. */
  showStatus?: boolean
}

/** The card as the canvas draws it (frame 07): chips, title, due, checklist, points, avatars. */
function TaskCardContent({ card, status, href, onMove, handle, showStatus }: ContentProps) {
  const due = formatShortDay(card.dueAt)
  return (
    <>
      <div className="flex flex-wrap items-center gap-1.5">
        <Badge color="gray">{card.committeeName}</Badge>
        {showStatus ? <Badge color="blue">{STATUS_LABELS[status]}</Badge> : null}
        {card.dueSoon ? <Badge color="red">Due soon</Badge> : null}
        {card.overdue && !card.dueSoon ? <Badge color="red">Overdue</Badge> : null}
      </div>

      <Link
        to={href}
        className="text-sm leading-snug font-medium text-gray-900 after:absolute after:inset-0 after:rounded-md hover:text-brand-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600 dark:text-gray-50 dark:hover:text-brand-300"
      >
        {card.title}
        <span className="sr-only"> · {card.reference}</span>
      </Link>

      <div className="flex items-center gap-2.5 text-xs text-gray-600 dark:text-gray-400">
        {due ? <span>{due}</span> : null}
        <span className="inline-flex items-center gap-1">
          <CheckSquareIcon size={12} />
          <span>
            {card.checklistDone}/{card.checklistTotal}
            <span className="sr-only"> checklist items done</span>
          </span>
        </span>
        <span className="ml-auto rounded-md bg-brand-50 px-2 py-0.5 font-semibold text-brand-700 tabular-nums dark:bg-brand-300/10 dark:text-brand-300">
          {formatPoints(card.basePoints)} pts
        </span>
      </div>

      <div className="flex items-center gap-2">
        <div className="flex items-center pr-1.5">
          {card.assignees.map((a, i) => (
            <RoleAvatar key={`${a.name}-${i}`} initials={a.initials} name={a.name} role={a.role} isVolunteer={a.isVolunteer} overlap />
          ))}
        </div>
        {card.canMove && onMove ? (
          <div className="relative z-10 ml-auto flex items-center gap-1">
            {handle}
            <Menu aria-label={`Move ${card.title}`} label="Move to…" size="sm" items={moveItems(status, onMove)}>
              Move to…
            </Menu>
          </div>
        ) : null}
      </div>
    </>
  )
}

function moveItems(status: TaskStatus, onMove: (status: TaskStatus) => void) {
  return TASK_STATUSES.map((s) => ({
    label: STATUS_LABELS[s],
    selected: s === status,
    disabled: s === status,
    onSelect: () => onMove(s),
  }))
}

/** Board card: sortable (pointer drag + dnd-kit keyboard support through the grip button). */
export function TaskCardTile(props: ContentProps & { onMove: (status: TaskStatus) => void }) {
  const { card, status } = props
  const { setNodeRef, attributes, listeners, transform, transition, isDragging } = useSortable({
    id: card.id,
    data: { status },
    disabled: !card.canMove,
  })
  const style = { transform: CSS.Translate.toString(transform), transition }

  const handle = (
    <button
      type="button"
      {...attributes}
      {...listeners}
      aria-label={`Drag ${card.title} to another column`}
      className="inline-flex size-8 cursor-grab touch-none items-center justify-center rounded-md text-gray-600 hover:bg-gray-100 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600 dark:text-gray-400 dark:hover:bg-white/5"
    >
      <GripIcon />
    </button>
  )

  return (
    <li ref={setNodeRef} style={style} className={cn(cardClass, isDragging && 'z-10 opacity-80 shadow-lg')}>
      <TaskCardContent {...props} handle={handle} />
    </li>
  )
}

/** The same card without drag and drop, for /my-tasks. */
export function PlainTaskCard(props: ContentProps) {
  return (
    <li className={cardClass}>
      <TaskCardContent {...props} />
    </li>
  )
}
