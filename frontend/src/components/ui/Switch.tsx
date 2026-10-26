import type { ComponentProps } from 'react'
import { cn } from '@/lib/cn'

type SwitchProps = Omit<ComponentProps<'button'>, 'onChange' | 'role' | 'type'> & {
  checked: boolean
  onChange: (checked: boolean) => void
}

/** On/off toggle (role="switch"). Give it a visible label via aria-labelledby or a wrapping label id. */
export function Switch({ checked, onChange, className, disabled, ...rest }: SwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        'relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors disabled:cursor-not-allowed disabled:opacity-60',
        checked ? 'bg-brand-600 dark:bg-brand-300' : 'bg-gray-300 dark:bg-white/20',
        className,
      )}
      {...rest}
    >
      <span
        aria-hidden="true"
        className={cn(
          'inline-block size-5 rounded-full bg-white shadow-sm ring-1 ring-gray-900/5 transition-transform',
          checked ? 'translate-x-5.5' : 'translate-x-0.5',
        )}
      />
    </button>
  )
}
