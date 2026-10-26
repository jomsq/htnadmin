import { Avatar } from '@/components/ui/Avatar'
import { cn } from '@/lib/cn'
import { formatPoints, type LeaderboardRow } from '../schemas'

/**
 * Medal styling per place. Color is never the only signal: the place label and the rank number
 * are written out in every column.
 */
const MEDALS = [
  { label: '1st place', ring: 'border-amber-600 dark:border-amber-500', text: 'text-amber-700 dark:text-amber-400', height: 'h-[132px]', order: 'order-2' },
  { label: '2nd place', ring: 'border-gray-500 dark:border-gray-400', text: 'text-gray-700 dark:text-gray-300', height: 'h-[96px]', order: 'order-1' },
  { label: '3rd place', ring: 'border-amber-800 dark:border-amber-600', text: 'text-amber-800 dark:text-amber-500', height: 'h-[76px]', order: 'order-3' },
] as const

/**
 * Top three (canvas frame 14): shown 2nd, 1st, 3rd with different plinth heights, while the DOM
 * order stays 1, 2, 3 for screen readers and keyboard users.
 */
export function Podium({ rows }: { rows: LeaderboardRow[] }) {
  const top = rows.slice(0, MEDALS.length)
  return (
    <section aria-label="Top three" className="w-full self-center">
      <ol className="grid max-w-[880px] grid-cols-3 items-end gap-2 sm:gap-4">
        {top.map((row, i) => {
          const medal = MEDALS[i]
          return (
            <li key={row.personId} className={cn('flex flex-col items-center gap-2.5', medal.order)}>
              <Avatar initials={row.initials} size="xl" tone="plain" ringClass={cn('border-[3px]', medal.ring)} />
              <div className="flex flex-col items-center text-center">
                <span className="text-[0.9375rem] font-semibold text-gray-900 dark:text-gray-50">{row.name}</span>
                <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
                  {row.level}
                  {row.committees ? ` · ${row.committees}` : ''}
                </span>
              </div>
              <div
                className={cn(
                  'flex w-full flex-col items-center justify-center gap-0.5 rounded-t-xl border border-b-0 border-gray-200 bg-white dark:border-white/10 dark:bg-gray-900',
                  medal.height,
                )}
              >
                <span className={cn('text-[1.75rem] font-bold tabular-nums', medal.text)}>{row.rank}</span>
                <span className="sr-only">{medal.label}</span>
                <span className="text-base font-semibold text-gray-900 tabular-nums dark:text-gray-50">{formatPoints(row.points)} pts</span>
              </div>
            </li>
          )
        })}
      </ol>
    </section>
  )
}
