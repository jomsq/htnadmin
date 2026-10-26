import type { ReactNode } from 'react'
import { cn } from '@/lib/cn'

export type AlertTone = 'red' | 'green' | 'yellow' | 'blue' | 'gray' | 'brand'

const tones: Record<AlertTone, string> = {
  red: 'bg-red-50 text-red-800 dark:bg-red-400/10 dark:text-red-200',
  green: 'bg-green-50 text-green-800 dark:bg-green-400/10 dark:text-green-200',
  yellow: 'bg-yellow-50 text-yellow-800 dark:bg-yellow-400/10 dark:text-yellow-200',
  blue: 'bg-blue-50 text-blue-800 dark:bg-blue-400/10 dark:text-blue-200',
  gray: 'bg-gray-50 text-gray-700 dark:bg-white/5 dark:text-gray-300',
  brand: 'bg-brand-50 text-red-900 dark:bg-brand-300/10 dark:text-red-200',
}

/** Inline message block (tint background, no side stripe). Errors use role="alert". */
export function Alert({
  tone = 'gray',
  children,
  action,
  className,
  live,
}: {
  tone?: AlertTone
  children: ReactNode
  action?: ReactNode
  className?: string
  /** alert = assertive (errors), status = polite. Omit for static notes. */
  live?: 'alert' | 'status'
}) {
  return (
    <div role={live} className={cn('flex flex-wrap items-center gap-3 rounded-md px-3 py-2.5 text-[0.8125rem] leading-normal', tones[tone], className)}>
      <div className="min-w-0 flex-1">{children}</div>
      {action}
    </div>
  )
}
