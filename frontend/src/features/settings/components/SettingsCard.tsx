import type { ReactNode } from 'react'
import { Card } from '@/components/ui/Card'

/** One settings section: a card with an H2 and a one-line description. */
export function SettingsCard({ id, title, description, action, children }: { id: string; title: string; description: ReactNode; action?: ReactNode; children: ReactNode }) {
  const headingId = `${id}-heading`
  return (
    <Card id={id} aria-labelledby={headingId} className="flex flex-col gap-6 p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">
          <h2 id={headingId} className="text-base font-semibold text-gray-900 dark:text-gray-50">
            {title}
          </h2>
          <p className="text-sm text-gray-600 dark:text-gray-400">{description}</p>
        </div>
        {action}
      </div>
      {children}
    </Card>
  )
}
