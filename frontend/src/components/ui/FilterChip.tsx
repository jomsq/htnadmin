import type { ComponentProps } from 'react'
import { cn } from '@/lib/cn'

/** Toggle chip for list filters (aria-pressed). */
export function FilterChip({ selected, className, ...rest }: ComponentProps<'button'> & { selected: boolean }) {
  return (
    <button
      type="button"
      aria-pressed={selected}
      className={cn(
        'inline-flex h-9 items-center gap-1 rounded-md px-3 text-[0.8125rem] font-medium ring-1 ring-inset transition-colors',
        selected
          ? 'bg-brand-50 text-brand-700 ring-brand-600 dark:bg-brand-300/10 dark:text-brand-300 dark:ring-brand-300'
          : 'bg-white text-gray-700 ring-gray-300 hover:bg-gray-50 dark:bg-gray-900 dark:text-gray-200 dark:ring-white/15 dark:hover:bg-gray-800',
        className,
      )}
      {...rest}
    />
  )
}
