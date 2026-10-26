import type { UseQueryResult } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { ProgressBar } from '@/components/ui/ProgressBar'
import { Skeleton } from '@/components/ui/Spinner'
import { cn } from '@/lib/cn'
import { percentCheckedIn } from '../format'
import type { CheckInStats as Stats } from '../schemas'

type Props = {
  query: UseQueryResult<Stats>
  /** "This desk" on the desk, "This station" elsewhere. */
  stationLabel?: string
  /** compact = phone header (no station line). */
  compact?: boolean
  className?: string
}

/** Checked in / paid with a green progress bar, this station's count and stations online. */
export function CheckInStats({ query, stationLabel = 'This station', compact = false, className }: Props) {
  if (query.isPending) {
    return (
      <div role="status" aria-label="Loading check-in numbers" className={cn('flex flex-col gap-2', className)}>
        <Skeleton className="h-4 w-40" />
        <Skeleton className="h-2 w-full" />
      </div>
    )
  }
  if (query.isError) {
    return (
      <div className={cn('flex flex-wrap items-center justify-between gap-2 text-[0.8125rem] text-gray-600 dark:text-gray-300', className)}>
        <span>Could not load the check-in numbers.</span>
        <Button variant="secondary" size="lg" onClick={() => void query.refetch()}>
          Try again
        </Button>
      </div>
    )
  }
  const s = query.data
  const pct = percentCheckedIn(s.checkedIn, s.paid)
  return (
    <div className={cn('flex flex-col', compact ? 'gap-1.5' : 'gap-2', className)}>
      <div className={cn('flex justify-between gap-3', compact ? 'text-[0.8125rem]' : 'text-sm')}>
        <span className="text-gray-600 dark:text-gray-300">Checked in</span>
        <span className="font-semibold text-gray-900 tabular-nums dark:text-gray-50">
          {s.checkedIn} / {s.paid} paid
        </span>
      </div>
      <ProgressBar value={pct} tone="green" size={compact ? 'md' : 'lg'} label={`${s.checkedIn} of ${s.paid} paid attendees checked in`} />
      {compact ? null : (
        <div className="flex justify-between gap-3 text-[0.8125rem] text-gray-600 tabular-nums dark:text-gray-400">
          <span>
            {stationLabel}: {s.thisStation}
          </span>
          <span>
            {s.stationsOnline} {s.stationsOnline === 1 ? 'station' : 'stations'} online
          </span>
        </div>
      )}
    </div>
  )
}
