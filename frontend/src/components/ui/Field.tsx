import { useId, type ReactNode } from 'react'
import { cn } from '@/lib/cn'

export type FieldControlProps = {
  id: string
  'aria-describedby'?: string
  'aria-invalid'?: true
}

type FieldProps = {
  label: ReactNode
  /** Rendered at the right of the label row (for example a "Forgot password?" link). */
  labelAside?: ReactNode
  hint?: ReactNode
  error?: string
  optional?: boolean
  className?: string
  children: (control: FieldControlProps) => ReactNode
}

/** Visible label + hint + error, wiring aria-describedby and aria-invalid onto the control. */
export function Field({ label, labelAside, hint, error, optional, className, children }: FieldProps) {
  const id = useId()
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined

  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <div className="flex items-baseline justify-between gap-2">
        <label htmlFor={id} className="text-sm font-medium text-gray-700 dark:text-gray-200">
          {label}
          {optional ? <span className="font-normal text-gray-600 dark:text-gray-400"> (optional)</span> : null}
        </label>
        {labelAside}
      </div>
      {children({ id, 'aria-describedby': describedBy, 'aria-invalid': error ? true : undefined })}
      {hint ? (
        <p id={hintId} className="text-xs text-gray-600 dark:text-gray-400">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={errorId} className="text-xs font-medium text-red-700 dark:text-red-400">
          {error}
        </p>
      ) : null}
    </div>
  )
}
