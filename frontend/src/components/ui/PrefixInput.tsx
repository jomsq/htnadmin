import type { ReactNode } from 'react'
import { cn } from '@/lib/cn'
import { Input, type InputProps } from './Input'

/** Input with a fixed prefix (e.g. "+63" before a mobile number). The prefix is part of the visible label context only. */
export function PrefixInput({ prefix, className, controlSize = 'md', ...rest }: InputProps & { prefix: ReactNode }) {
  return (
    <div className={cn('flex w-full', className)}>
      <span
        aria-hidden="true"
        className={cn(
          'inline-flex shrink-0 items-center rounded-l-md bg-gray-50 px-3 text-gray-700 ring-1 ring-gray-300 ring-inset dark:bg-gray-800 dark:text-gray-300 dark:ring-white/15',
          controlSize === 'xl' ? 'h-12 text-base' : controlSize === 'lg' ? 'h-11 text-[0.9375rem]' : 'h-10 text-sm',
        )}
      >
        {prefix}
      </span>
      <Input controlSize={controlSize} className="-ml-px min-w-0 flex-1 rounded-l-none" {...rest} />
    </div>
  )
}
