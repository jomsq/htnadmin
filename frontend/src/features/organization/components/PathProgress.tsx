import { cn } from '@/lib/cn'
import { levelRank, pathLevels, type Level } from '../schemas'

/** Member → Apprentice → Goon as filled bars up to the person's step (canvas frame 02). Board fills the whole path. */
export function PathProgress({ level }: { level: Level }) {
  const current = levelRank[level]
  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-[0.8125rem] font-semibold text-gray-700 dark:text-gray-300">Path</h3>
      <ol aria-label="Path progress" className="flex gap-1.5">
        {pathLevels.map((step) => {
          const reached = levelRank[step] <= current
          const here = step === level
          return (
            <li key={step} aria-current={here ? 'step' : undefined} className="flex flex-1 flex-col gap-1.5">
              <span
                aria-hidden="true"
                className={cn('h-1.5 rounded-full', reached ? 'bg-brand-600 dark:bg-brand-300' : 'bg-gray-200 dark:bg-white/10')}
              />
              <span className={cn('text-xs font-medium', reached ? 'text-gray-900 dark:text-gray-50' : 'text-gray-600 dark:text-gray-400')}>
                {step}
                {reached && !here ? <span className="sr-only"> (done)</span> : null}
              </span>
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
