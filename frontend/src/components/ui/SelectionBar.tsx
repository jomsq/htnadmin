import type { ReactNode } from 'react'

/** Dark bar shown while table rows are selected (canvas frame 09): "N selected · Total ₱X" and bulk actions. */
export function SelectionBar({ label = 'Bulk actions', summary, children }: { label?: string; summary: ReactNode; children: ReactNode }) {
  return (
    <div
      role="region"
      aria-label={label}
      className="flex flex-wrap items-center gap-3 rounded-lg bg-gray-900 px-4 py-2.5 text-white ring-1 ring-white/10 dark:bg-gray-800"
    >
      <div aria-live="polite" className="flex flex-wrap items-baseline gap-3">
        {summary}
      </div>
      <div className="ml-auto flex flex-wrap gap-2">{children}</div>
    </div>
  )
}
