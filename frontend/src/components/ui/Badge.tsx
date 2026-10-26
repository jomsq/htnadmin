import type { ReactNode } from 'react'
import { cn } from '@/lib/cn'

export type BadgeColor = 'green' | 'yellow' | 'blue' | 'red' | 'gray' | 'teal' | 'brand'

const light: Record<BadgeColor, string> = {
  green: 'bg-green-50 text-green-700',
  yellow: 'bg-yellow-50 text-yellow-800',
  blue: 'bg-blue-50 text-blue-700',
  red: 'bg-red-50 text-red-700',
  gray: 'bg-gray-100 text-gray-700',
  teal: 'bg-teal-50 text-teal-700',
  brand: 'bg-brand-50 text-brand-700',
}

const dark: Record<BadgeColor, string> = {
  green: 'dark:bg-green-500/10 dark:text-green-400',
  yellow: 'dark:bg-yellow-400/10 dark:text-yellow-300',
  blue: 'dark:bg-blue-400/10 dark:text-blue-300',
  red: 'dark:bg-red-400/10 dark:text-red-300',
  gray: 'dark:bg-white/10 dark:text-gray-300',
  teal: 'dark:bg-teal-400/10 dark:text-teal-300',
  brand: 'dark:bg-brand-300/10 dark:text-brand-300',
}

/**
 * Status badge: tint background + 700 text. `solid` keeps the light-mode tint in both themes,
 * for badges that sit on a cover image or color rather than on a card surface.
 */
export function Badge({ color = 'gray', solid = false, children, className }: { color?: BadgeColor; solid?: boolean; children: ReactNode; className?: string }) {
  return (
    <span className={cn('inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium whitespace-nowrap', light[color], !solid && dark[color], className)}>
      {children}
    </span>
  )
}
