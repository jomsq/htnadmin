import { cn } from '@/lib/cn'

/** Thin brand bar (h-1.5, gray track), e.g. tickets sold against capacity. `value` is 0–100. */
export function ProgressBar({ value, label, className }: { value: number; label: string; className?: string }) {
  const pct = Math.max(0, Math.min(100, Math.round(value)))
  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={pct}
      aria-valuetext={`${pct}%`}
      className={cn('h-1.5 overflow-hidden rounded-full bg-gray-100 dark:bg-white/10', className)}
    >
      <div className="h-full rounded-full bg-brand-600 dark:bg-brand-300" style={{ width: `${pct}%` }} />
    </div>
  )
}
