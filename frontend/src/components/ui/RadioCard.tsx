import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/cn'

type RadioCardProps = Omit<ComponentProps<'input'>, 'type' | 'title'> & {
  title: ReactNode
  description?: ReactNode
  /** Right-aligned value, e.g. the price. */
  aside?: ReactNode
}

/**
 * A native radio inside a full-width label card (canvas frame 10 ticket picker).
 * Selected: brand ring + tint. Disabled: gray, not clickable, still readable.
 */
export function RadioCard({ title, description, aside, className, checked, disabled, ...input }: RadioCardProps) {
  return (
    <label
      className={cn(
        'flex min-h-14 items-center gap-3 rounded-lg p-3.5 ring-inset transition-colors',
        disabled
          ? 'cursor-not-allowed bg-gray-50 ring-1 ring-gray-200 dark:bg-white/5 dark:ring-white/10'
          : checked
            ? 'cursor-pointer bg-brand-50 ring-2 ring-brand-600 dark:bg-brand-300/10 dark:ring-brand-300'
            : 'cursor-pointer bg-white ring-1 ring-gray-300 hover:bg-gray-50 dark:bg-gray-900 dark:ring-white/15 dark:hover:bg-gray-800',
        className,
      )}
    >
      <input type="radio" checked={checked} disabled={disabled} className="size-[18px] shrink-0 accent-brand-600 dark:accent-brand-300" {...input} />
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className={cn('text-[0.9375rem] font-semibold', disabled ? 'text-gray-600 dark:text-gray-400' : 'text-gray-900 dark:text-gray-50')}>{title}</span>
        {description ? <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">{description}</span> : null}
      </span>
      {aside ? (
        <span className={cn('text-[0.9375rem] font-semibold tabular-nums', disabled ? 'text-gray-600 dark:text-gray-400' : 'text-gray-900 dark:text-gray-50')}>
          {aside}
        </span>
      ) : null}
    </label>
  )
}
