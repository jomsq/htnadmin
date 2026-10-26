import { cn } from '@/lib/cn'

export type ProgressTone = 'brand' | 'green'

const fills: Record<ProgressTone, string> = {
  brand: 'bg-brand-600 dark:bg-brand-300',
  green: 'bg-green-600 dark:bg-green-400',
}

/**
 * Thin bar (gray track), e.g. tickets sold against capacity. `value` is 0–100.
 * green = progress toward done (check-in); size lg = 8px for kiosk screens.
 */
export function ProgressBar({
  value,
  label,
  tone = 'brand',
  size = 'md',
  className,
}: {
  value: number
  label: string
  tone?: ProgressTone
  size?: 'md' | 'lg'
  className?: string
}) {
  const pct = Math.max(0, Math.min(100, Math.round(value)))
  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={pct}
      aria-valuetext={`${pct}%`}
      className={cn(size === 'lg' ? 'h-2' : 'h-1.5', 'overflow-hidden rounded-full bg-gray-100 dark:bg-white/10', className)}
    >
      <div className={cn('h-full rounded-full', fills[tone])} style={{ width: `${pct}%` }} />
    </div>
  )
}
