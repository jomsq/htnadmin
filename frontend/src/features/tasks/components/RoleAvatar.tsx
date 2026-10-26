import { cn } from '@/lib/cn'
import type { TaskRole } from '../schemas'

/**
 * Role styles for avatars. Each role differs in lightness as well as hue, and the role is always
 * named in the accessible name, so colour is never the only signal (design-system accessibility).
 */
const roleStyles: Record<TaskRole, string> = {
  Lead: 'bg-blue-700 text-white ring-blue-900 dark:bg-blue-500 dark:ring-blue-200',
  Collab: 'bg-teal-100 text-teal-900 ring-teal-600 dark:bg-teal-800 dark:text-teal-50 dark:ring-teal-300',
  Assistant: 'bg-yellow-50 text-yellow-900 ring-yellow-500 dark:bg-yellow-900 dark:text-yellow-50 dark:ring-yellow-400',
}

const sizes = { sm: 'size-6.5 text-[0.625rem]', md: 'size-8 text-xs' } as const

type Props = {
  initials: string
  name: string
  role: TaskRole
  isVolunteer?: boolean
  size?: keyof typeof sizes
  /** Overlapping stack on the board cards. */
  overlap?: boolean
  className?: string
}

export function RoleAvatar({ initials, name, role, isVolunteer, size = 'sm', overlap, className }: Props) {
  const label = `${name} · ${role}${isVolunteer ? ' · Volunteer' : ''}`
  return (
    <span
      role="img"
      aria-label={label}
      title={label}
      className={cn(
        'inline-flex shrink-0 items-center justify-center rounded-full font-semibold ring-2',
        sizes[size],
        roleStyles[role],
        overlap && '-mr-1.5 outline-2 outline-white dark:outline-gray-900',
        className,
      )}
    >
      {initials}
    </span>
  )
}
