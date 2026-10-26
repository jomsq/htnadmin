import { CheckIcon } from '@/components/ui/icons'
import { cn } from '@/lib/cn'
import { levelRank, pathLevels, type Level } from '../schemas'

/** Member → Apprentice → Goon, with the person's step marked. Board is a separate seat. */
export function PathProgress({ level }: { level: Level }) {
  const current = levelRank[level]
  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-[0.8125rem] font-semibold text-gray-700 dark:text-gray-300">Path</h3>
      <ol aria-label="Path progress" className="grid grid-cols-3 gap-1.5">
        {pathLevels.map((step) => {
          const rank = levelRank[step]
          const done = rank < current
          const here = step === level
          return (
            <li
              key={step}
              aria-current={here ? 'step' : undefined}
              className={cn(
                'flex h-9 items-center justify-center gap-1.5 rounded-md text-[0.8125rem] font-medium ring-1 ring-inset',
                here
                  ? 'bg-brand-50 text-brand-700 ring-brand-600 dark:bg-brand-300/10 dark:text-brand-300 dark:ring-brand-300'
                  : done
                    ? 'bg-gray-50 text-gray-700 ring-gray-200 dark:bg-white/5 dark:text-gray-300 dark:ring-white/10'
                    : 'bg-white text-gray-600 ring-gray-200 dark:bg-gray-900 dark:text-gray-400 dark:ring-white/10',
              )}
            >
              {done ? <CheckIcon size={14} /> : null}
              {step}
              {done ? <span className="sr-only">(done)</span> : null}
            </li>
          )
        })}
      </ol>
      {level === 'Board' ? (
        <p className="text-xs text-gray-600 dark:text-gray-400">Board of Trustees seat. The board sits outside the path.</p>
      ) : null}
    </div>
  )
}
