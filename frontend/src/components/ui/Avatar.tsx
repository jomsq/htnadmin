import { cn } from '@/lib/cn'

const sizes = { sm: 'size-8 text-xs', lg: 'size-13 text-base' } as const

export function Avatar({ initials, size = 'sm', tone = 'gray' }: { initials: string; size?: keyof typeof sizes; tone?: 'gray' | 'blue' | 'brand' }) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        'inline-flex shrink-0 items-center justify-center rounded-full font-semibold',
        sizes[size],
        tone === 'gray' && 'bg-gray-100 text-gray-700 dark:bg-white/10 dark:text-gray-200',
        tone === 'blue' && 'bg-blue-50 text-blue-700 dark:bg-blue-400/10 dark:text-blue-300',
        tone === 'brand' && 'bg-brand-50 text-brand-700 dark:bg-brand-300/10 dark:text-brand-300',
      )}
    >
      {initials}
    </span>
  )
}
