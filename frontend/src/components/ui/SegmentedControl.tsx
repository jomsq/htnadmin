import { useRef, type KeyboardEvent, type ReactNode } from 'react'
import { cn } from '@/lib/cn'

export type SegmentOption<T extends string> = {
  value: T
  label: string
  /** Icon-only segment: the label becomes aria-label and title (tooltip). */
  icon?: ReactNode
}

type Props<T extends string> = {
  options: ReadonlyArray<SegmentOption<T>>
  value: T
  onChange: (value: T) => void
  'aria-label'?: string
  'aria-labelledby'?: string
  'aria-describedby'?: string
  /** segmented = gray track with white selected pill; chips = separate ring buttons (level picker). */
  variant?: 'segmented' | 'chips'
  /** lg = 44px targets (public and kiosk screens). */
  size?: 'md' | 'lg'
  /** Stretch options into equal grid columns. */
  fill?: boolean
  className?: string
}

/** Single-select radiogroup with roving focus (arrow keys). */
export function SegmentedControl<T extends string>({
  options,
  value,
  onChange,
  variant = 'segmented',
  size = 'md',
  fill = false,
  className,
  ...aria
}: Props<T>) {
  // Keep one option reachable with Tab even when nothing is selected yet.
  const focusIndex = Math.max(0, options.findIndex((o) => o.value === value))
  const refs = useRef<Array<HTMLButtonElement | null>>([])

  function onKeyDown(e: KeyboardEvent<HTMLButtonElement>, index: number) {
    const delta = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0
    if (!delta) return
    e.preventDefault()
    const next = (index + delta + options.length) % options.length
    onChange(options[next].value)
    refs.current[next]?.focus()
  }

  return (
    <div
      role="radiogroup"
      {...aria}
      style={fill ? { gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))` } : undefined}
      className={cn(
        fill ? 'grid' : 'inline-flex flex-wrap',
        variant === 'segmented' ? 'gap-0.5 rounded-lg bg-gray-100 p-1 dark:bg-white/5' : 'gap-1.5',
        className,
      )}
    >
      {options.map((opt, i) => {
        const checked = opt.value === value
        return (
          <button
            key={opt.value}
            ref={(el) => {
              refs.current[i] = el
            }}
            type="button"
            role="radio"
            aria-checked={checked}
            aria-label={opt.icon ? opt.label : undefined}
            title={opt.icon ? opt.label : undefined}
            tabIndex={i === focusIndex ? 0 : -1}
            onClick={() => onChange(opt.value)}
            onKeyDown={(e) => onKeyDown(e, i)}
            className={cn(
              'inline-flex items-center justify-center rounded-md font-medium transition-colors',
              size === 'lg' ? 'h-11 text-sm font-semibold' : 'h-9 text-[0.8125rem]',
              opt.icon ? 'w-11' : 'px-3',
              variant === 'segmented'
                ? checked
                  ? 'bg-white text-gray-900 shadow-sm dark:bg-gray-700 dark:text-gray-50'
                  : 'text-gray-600 hover:text-gray-900 dark:text-gray-400 dark:hover:text-gray-100'
                : checked
                  ? 'bg-brand-50 text-brand-700 ring-1 ring-brand-600 ring-inset dark:bg-brand-300/10 dark:text-brand-300 dark:ring-brand-300'
                  : 'bg-white text-gray-700 ring-1 ring-gray-300 ring-inset hover:bg-gray-50 dark:bg-gray-900 dark:text-gray-200 dark:ring-white/15 dark:hover:bg-gray-800',
            )}
          >
            {opt.icon ?? opt.label}
          </button>
        )
      })}
    </div>
  )
}
