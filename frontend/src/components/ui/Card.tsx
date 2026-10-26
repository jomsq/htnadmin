import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/cn'

export const cardClass = 'rounded-lg bg-white shadow-sm ring-1 ring-gray-900/5 dark:bg-gray-900 dark:ring-white/10'

export function Card({ className, ...rest }: ComponentProps<'section'>) {
  return <section className={cn(cardClass, className)} {...rest} />
}

export function CardHeader({
  title,
  description,
  action,
  as: Heading = 'h2',
}: {
  title: ReactNode
  description?: ReactNode
  action?: ReactNode
  as?: 'h2' | 'h3'
}) {
  return (
    <div className="flex items-center justify-between gap-4 border-b border-gray-200 px-5 py-4 dark:border-white/10">
      <div className="flex min-w-0 flex-col gap-0.5">
        <Heading className="text-base font-semibold text-gray-900 dark:text-gray-50">{title}</Heading>
        {description ? <p className="text-sm text-gray-600 dark:text-gray-400">{description}</p> : null}
      </div>
      {action}
    </div>
  )
}

export function CardBody({ className, ...rest }: ComponentProps<'div'>) {
  return <div className={cn('p-5', className)} {...rest} />
}
