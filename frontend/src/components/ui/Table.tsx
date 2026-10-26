import type { ComponentProps } from 'react'
import { cn } from '@/lib/cn'

/** Table wrapped in an overflow-x-auto container. Row padding follows the density setting. */
export function Table({ className, minWidth = 640, ...rest }: ComponentProps<'table'> & { minWidth?: number }) {
  return (
    <div className="overflow-x-auto">
      <table style={{ minWidth }} className={cn('w-full border-collapse text-left text-sm', className)} {...rest} />
    </div>
  )
}

export function THead({ className, ...rest }: ComponentProps<'thead'>) {
  return <thead className={cn('bg-gray-50 text-xs font-semibold text-gray-600 dark:bg-white/5 dark:text-gray-400', className)} {...rest} />
}

export function TBody(props: ComponentProps<'tbody'>) {
  return <tbody {...props} />
}

export function Tr({ className, ...rest }: ComponentProps<'tr'>) {
  return <tr className={cn('border-t border-gray-100 dark:border-white/5', className)} {...rest} />
}

export function Th({ className, scope = 'col', ...rest }: ComponentProps<'th'>) {
  return <th scope={scope} className={cn('px-3 py-2.5 font-semibold first:pl-5 last:pr-5', className)} {...rest} />
}

export function Td({ className, numeric, ...rest }: ComponentProps<'td'> & { numeric?: boolean }) {
  return (
    <td
      className={cn(
        'px-3 py-3 text-gray-700 first:pl-5 last:pr-5 compact:py-2 dark:text-gray-300',
        numeric && 'text-right tabular-nums',
        className,
      )}
      {...rest}
    />
  )
}
