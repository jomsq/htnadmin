import type { UseQueryResult } from '@tanstack/react-query'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Skeleton } from '@/components/ui/Spinner'
import { cn } from '@/lib/cn'
import { formatAgo, recentLabels, recentTones, type ResultTone } from '../format'
import type { RecentScan } from '../schemas'

const dots: Record<ResultTone, string> = {
  green: 'bg-green-600 dark:bg-green-400',
  yellow: 'bg-amber-600 dark:bg-yellow-400',
  red: 'bg-red-600 dark:bg-red-400',
  gray: 'bg-gray-400',
}

const labelColors: Record<ResultTone, string> = {
  green: 'text-green-700 dark:text-green-400',
  yellow: 'text-yellow-800 dark:text-yellow-300',
  red: 'text-red-700 dark:text-red-400',
  gray: 'text-gray-600 dark:text-gray-400',
}

/** Last scans at this station (the server returns up to 20). Each color dot carries a text label. */
export function RecentScans({ query }: { query: UseQueryResult<RecentScan[]> }) {
  return (
    <Card aria-labelledby="recent-scans-heading" className="flex flex-col">
      <h2 id="recent-scans-heading" className="border-b border-gray-200 px-5 py-3.5 text-sm font-semibold dark:border-white/10">
        Recent scans
      </h2>
      {query.isPending ? (
        <div role="status" aria-label="Loading recent scans" className="flex flex-col gap-2 px-5 py-4">
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-3/4" />
        </div>
      ) : query.isError ? (
        <div className="p-4">
          <Alert
            tone="red"
            action={
              <Button variant="secondary" size="lg" onClick={() => void query.refetch()}>
                Try again
              </Button>
            }
          >
            Could not load recent scans. {errorMessage(query.error)}
          </Alert>
        </div>
      ) : query.data.length === 0 ? (
        <p className="px-5 py-6 text-center text-[0.8125rem] text-gray-600 dark:text-gray-400">No scans at this station yet.</p>
      ) : (
        <ol className="flex flex-col">
          {query.data.map((x, i) => {
            const tone = recentTones[x.result]
            return (
              <li
                key={`${x.at}-${i}`}
                className="flex items-center gap-2.5 border-b border-gray-100 px-5 py-2.5 text-[0.8125rem] last:border-b-0 dark:border-white/5"
              >
                <span aria-hidden="true" className={cn('size-2 flex-none rounded-full', dots[tone])} />
                <span className="min-w-0 flex-1">
                  <span className="font-medium">{x.name ?? 'Unknown code'}</span>{' '}
                  <span className="font-mono text-gray-600 dark:text-gray-400">{x.participantId ?? '—'}</span>
                  <span className={cn('block text-xs font-medium', labelColors[tone])}>{recentLabels[x.result]}</span>
                </span>
                <span className="text-gray-600 tabular-nums dark:text-gray-400">{formatAgo(x.at)}</span>
              </li>
            )
          })}
        </ol>
      )}
    </Card>
  )
}
