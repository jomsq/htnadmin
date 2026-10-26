import { NavLink } from 'react-router'
import { cn } from '@/lib/cn'

export type TabLink = { to: string; label: string; end?: boolean }

/** Underline tabs for sibling routes (border-b-2 border-brand-600 text-brand-700 on the active one). */
export function TabNav({ tabs, 'aria-label': label, className }: { tabs: ReadonlyArray<TabLink>; 'aria-label': string; className?: string }) {
  return (
    <nav aria-label={label} className={cn('border-b border-gray-200 dark:border-white/10', className)}>
      <ul className="-mb-px flex gap-6 overflow-x-auto">
        {tabs.map((t) => (
          <li key={t.to}>
            <NavLink
              to={t.to}
              end={t.end}
              className={({ isActive }) =>
                cn(
                  'inline-flex h-11 items-center border-b-2 px-1 text-sm font-medium whitespace-nowrap transition-colors',
                  isActive
                    ? 'border-brand-600 text-brand-700 dark:border-brand-300 dark:text-brand-300'
                    : 'border-transparent text-gray-600 hover:border-gray-300 hover:text-gray-900 dark:text-gray-400 dark:hover:text-gray-100',
                )
              }
            >
              {t.label}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}
