import type { ReactNode } from 'react'
import { cardClass } from './Card'
import { cn } from '@/lib/cn'

/** KPI card: label, value (text-2xl), sub line. */
export function Stat({ label, value, sub }: { label: ReactNode; value: ReactNode; sub?: ReactNode }) {
  return (
    <div className={cn(cardClass, 'flex flex-col gap-2 p-5')}>
      <span className="text-[0.8125rem] font-medium text-gray-600 dark:text-gray-400">{label}</span>
      <span className="text-2xl font-semibold tracking-tight text-gray-900 tabular-nums dark:text-gray-50">{value}</span>
      {sub ? <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">{sub}</span> : null}
    </div>
  )
}
