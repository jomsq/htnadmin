import type { FieldControlProps } from './Field'
import { cn } from '@/lib/cn'
import { controlClass } from './Input'
import { MinusIcon, PlusIcon } from './icons'

type StepperProps = Partial<FieldControlProps> & {
  value: number
  onChange: (value: number) => void
  onBlur?: () => void
  min: number
  max: number
  step: number
  /** Decimal places kept when stepping (avoids 0.1 + 0.2 drift). */
  precision?: number
  /** Accessible name used for the − / + buttons, e.g. "Lead multiplier". */
  name: string
  className?: string
}

const round = (n: number, precision: number) => Number(n.toFixed(precision))

/** Number input with − / + buttons (44px targets). Typing is allowed; buttons clamp to [min, max]. */
export function Stepper({ value, onChange, onBlur, min, max, step, precision = 0, name, className, ...control }: StepperProps) {
  const safe = Number.isFinite(value) ? value : min
  const set = (n: number) => onChange(round(Math.min(max, Math.max(min, n)), precision))
  const btn =
    'inline-flex size-11 shrink-0 items-center justify-center rounded-md text-gray-700 ring-1 ring-inset ring-gray-300 bg-white hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-gray-900 dark:text-gray-200 dark:ring-white/15 dark:hover:bg-gray-800'
  return (
    <div className={cn('flex items-center gap-1.5', className)}>
      <button type="button" className={btn} aria-label={`Decrease ${name}`} disabled={safe <= min} onClick={() => set(safe - step)}>
        <MinusIcon size={16} />
      </button>
      <input
        {...control}
        type="number"
        inputMode="decimal"
        min={min}
        max={max}
        step={step}
        value={Number.isFinite(value) ? value : ''}
        onChange={(e) => onChange(e.target.value === '' ? Number.NaN : Number(e.target.value))}
        onBlur={onBlur}
        className={cn(controlClass, 'h-11 w-20 text-center tabular-nums')}
      />
      <button type="button" className={btn} aria-label={`Increase ${name}`} disabled={safe >= max} onClick={() => set(safe + step)}>
        <PlusIcon size={16} />
      </button>
    </div>
  )
}
