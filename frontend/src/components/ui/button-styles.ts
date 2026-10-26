import { cn } from '@/lib/cn'

/** danger = red text on a secondary button; destructive = solid red for confirm dialogs. */
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'destructive' | 'link'
export type ButtonSize = 'sm' | 'md' | 'lg' | 'icon'

const base =
  'inline-flex items-center justify-center gap-2 rounded-md font-semibold whitespace-nowrap transition-colors disabled:cursor-not-allowed disabled:opacity-60'

export const buttonVariants: Record<ButtonVariant, string> = {
  primary: 'bg-brand-600 text-white shadow-sm hover:bg-brand-700',
  secondary:
    'bg-white text-gray-700 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50 dark:bg-gray-900 dark:text-gray-100 dark:ring-white/15 dark:hover:bg-gray-800',
  ghost: 'text-gray-700 hover:bg-gray-100 dark:text-gray-300 dark:hover:bg-gray-800',
  danger:
    'bg-white text-red-700 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-red-50 dark:bg-gray-900 dark:text-red-400 dark:ring-white/15 dark:hover:bg-red-950/40',
  destructive: 'bg-red-700 text-white shadow-sm hover:bg-red-800 dark:bg-red-600 dark:hover:bg-red-700',
  link: 'px-0 text-brand-600 hover:text-brand-700 dark:text-brand-300 dark:hover:text-brand-300',
}

export const buttonSizes: Record<ButtonSize, string> = {
  sm: 'h-8 px-3 text-xs',
  md: 'h-10 px-4 text-sm',
  lg: 'h-11 px-4 text-[0.9375rem]',
  icon: 'size-10 text-sm',
}

export function buttonClass(variant: ButtonVariant = 'primary', size: ButtonSize = 'md', className?: string) {
  return cn(base, buttonSizes[size], buttonVariants[variant], variant === 'link' && 'h-auto', className)
}

/** Button styles for use on the dark SelectionBar. */
export function selectionBarButtonClass(primary = false, className?: string) {
  return cn(
    buttonClass('ghost', 'md', 'h-9 px-3 text-[0.8125rem]'),
    primary
      ? 'bg-white text-gray-900 hover:bg-gray-100 dark:text-gray-900 dark:hover:bg-gray-200'
      : 'text-white ring-1 ring-gray-600 ring-inset hover:bg-white/10 dark:text-white dark:hover:bg-white/10',
    className,
  )
}
