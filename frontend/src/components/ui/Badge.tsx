import type { ReactNode } from 'react'
import { cn } from '@/lib/cn'

export type BadgeColor = 'green' | 'yellow' | 'blue' | 'red' | 'gray' | 'teal' | 'brand'

const colors: Record<BadgeColor, string> = {
  green: 'bg-green-50 text-green-700 dark:bg-green-500/10 dark:text-green-400',
  yellow: 'bg-yellow-50 text-yellow-800 dark:bg-yellow-400/10 dark:text-yellow-300',
  blue: 'bg-blue-50 text-blue-700 dark:bg-blue-400/10 dark:text-blue-300',
  red: 'bg-red-50 text-red-700 dark:bg-red-400/10 dark:text-red-300',
  gray: 'bg-gray-100 text-gray-700 dark:bg-white/10 dark:text-gray-300',
  teal: 'bg-teal-50 text-teal-700 dark:bg-teal-400/10 dark:text-teal-300',
  brand: 'bg-brand-50 text-brand-700 dark:bg-brand-300/10 dark:text-brand-300',
}

export function Badge({ color = 'gray', children, className }: { color?: BadgeColor; children: ReactNode; className?: string }) {
  return (
    <span className={cn('inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium whitespace-nowrap', colors[color], className)}>
      {children}
    </span>
  )
}
