import { CheckIcon } from '@/components/ui/icons'
import { cn } from '@/lib/cn'

export type SetupStep = { id: string; label: string; complete: boolean }

/** Left section stepper on Details (canvas frame 05): ✓ when a step is complete, its number otherwise. */
export function SetupSteps({ steps, active, onSelect }: { steps: readonly SetupStep[]; active: string; onSelect: (id: string) => void }) {
  return (
    <nav aria-label="Setup steps" className="w-full md:max-w-60 md:flex-[1_1_200px]">
      <ol className="flex gap-1 overflow-x-auto md:flex-col">
        {steps.map((s, i) => {
          const current = s.id === active
          return (
            <li key={s.id} className="shrink-0">
              <a
                href={`#${s.id}`}
                aria-current={current ? 'step' : undefined}
                onClick={() => onSelect(s.id)}
                className={cn(
                  'flex min-h-11 items-center gap-2.5 rounded-md px-3 text-sm whitespace-nowrap',
                  current
                    ? 'bg-white font-semibold text-gray-900 ring-1 ring-gray-200 dark:bg-gray-900 dark:text-gray-50 dark:ring-white/10'
                    : 'font-medium text-gray-700 hover:bg-white hover:text-gray-900 dark:text-gray-300 dark:hover:bg-gray-900 dark:hover:text-gray-50',
                )}
              >
                {s.complete ? (
                  <span className="inline-flex size-5.5 shrink-0 items-center justify-center rounded-full bg-green-700 text-white dark:bg-green-600">
                    <CheckIcon size={13} strokeWidth={2.5} />
                  </span>
                ) : (
                  <span
                    aria-hidden="true"
                    className={cn(
                      'inline-flex size-5.5 shrink-0 items-center justify-center rounded-full border-2 text-[0.6875rem] font-semibold',
                      current ? 'border-brand-600 text-brand-700 dark:border-brand-300 dark:text-brand-300' : 'border-gray-300 text-gray-600 dark:border-white/20 dark:text-gray-400',
                    )}
                  >
                    {i + 1}
                  </span>
                )}
                {s.label}
                <span className="sr-only">{s.complete ? ' (complete)' : ' (to do)'}</span>
              </a>
            </li>
          )
        })}
      </ol>
    </nav>
  )
}
