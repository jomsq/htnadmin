import type { ReactNode } from 'react'
import { cn } from '@/lib/cn'

export function EmptyState({
  icon,
  title,
  description,
  action,
  className,
}: {
  icon?: ReactNode
  title: ReactNode
  description?: ReactNode
  action?: ReactNode
  className?: string
}) {
  return (
    <div className={cn('flex flex-col items-center gap-3 px-6 py-12 text-center', className)}>
      {icon ? (
        <div aria-hidden="true" className="flex size-12 items-center justify-center rounded-full bg-gray-100 text-gray-600 dark:bg-white/5 dark:text-gray-400">
          {icon}
        </div>
      ) : null}
      <div className="flex max-w-sm flex-col gap-1">
        <p className="text-sm font-semibold text-gray-900 dark:text-gray-50">{title}</p>
        {description ? <p className="text-sm text-gray-600 dark:text-gray-400">{description}</p> : null}
      </div>
      {action}
    </div>
  )
}
