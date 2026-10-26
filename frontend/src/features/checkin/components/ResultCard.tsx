import { Button } from '@/components/ui/Button'
import { CheckIcon, CloseIcon, ExclamationIcon, InfoIcon, QrIcon } from '@/components/ui/icons'
import { cn } from '@/lib/cn'
import { confirmableLookup, describeOutcome, type ResultTone, type ScanOutcome } from '../format'

const surfaces: Record<ResultTone, string> = {
  green: 'bg-green-50 text-green-900 ring-green-200 dark:bg-green-500/10 dark:text-green-100 dark:ring-green-500/30',
  yellow: 'bg-yellow-50 text-yellow-900 ring-yellow-200 dark:bg-yellow-400/10 dark:text-yellow-100 dark:ring-yellow-400/30',
  red: 'bg-red-50 text-red-900 ring-red-200 dark:bg-red-500/10 dark:text-red-100 dark:ring-red-500/30',
  blue: 'bg-blue-50 text-blue-900 ring-blue-200 dark:bg-blue-400/10 dark:text-blue-100 dark:ring-blue-400/30',
  gray: 'bg-white text-gray-900 ring-gray-900/5 dark:bg-gray-900 dark:text-gray-50 dark:ring-white/10',
}

const iconCircles: Record<ResultTone, string> = {
  green: 'bg-green-600 text-white',
  yellow: 'bg-amber-600 text-white',
  red: 'bg-red-600 text-white',
  blue: 'bg-blue-600 text-white',
  gray: 'bg-gray-100 text-gray-600 dark:bg-white/10 dark:text-gray-300',
}

function ToneIcon({ tone, size }: { tone: ResultTone; size: number }) {
  const props = { size, strokeWidth: 2.6 }
  if (tone === 'green') return <CheckIcon {...props} />
  if (tone === 'yellow') return <ExclamationIcon {...props} />
  if (tone === 'red') return <CloseIcon {...props} />
  if (tone === 'blue') return <InfoIcon size={size} strokeWidth={2.2} />
  return <QrIcon size={size} />
}

type Props = {
  outcome: ScanOutcome | null
  /** Changes on every response so the same result twice in a row is announced again. */
  outcomeKey?: number
  size: 'desk' | 'phone'
  className?: string
  /** Confirm a Lookup (typed or QR'd participant ID) for a paid attendee: POST /manual. */
  onConfirm?: (attendeeId: string) => void
  confirming?: boolean
}

/**
 * Live result of the last scan (aria-live assertive). Color always comes with a title, so it reads
 * the same without color. Desk: 72px icon circle and 28px title; phone: 44px and 18px.
 */
export function ResultCard({ outcome, outcomeKey = 0, size, className, onConfirm, confirming = false }: Props) {
  const desk = size === 'desk'
  const lookup = onConfirm ? confirmableLookup(outcome) : null
  return (
    <div className={cn('flex flex-col gap-3', className)}>
      <div role="status" aria-live="assertive" aria-atomic="true" aria-label="Scan result">
        {outcome ? (
          <ResultBody key={outcomeKey} outcome={outcome} desk={desk} />
        ) : desk ? (
          <div className={cn('flex min-h-40 items-center gap-5 rounded-lg p-6 shadow-sm ring-1', surfaces.gray)}>
            <span aria-hidden="true" className={cn('flex size-18 flex-none items-center justify-center rounded-full', iconCircles.gray)}>
              <QrIcon size={32} />
            </span>
            <div className="flex flex-col gap-1">
              <span className="text-[1.75rem] leading-tight font-bold tracking-tight">Ready to scan</span>
              <span className="text-[0.9375rem] text-gray-600 dark:text-gray-400">Waiting for a scan… The result shows here.</span>
            </div>
          </div>
        ) : (
          <div className="rounded-2xl border border-dashed border-gray-300 p-5 text-center text-sm text-gray-600 dark:border-gray-700 dark:text-gray-300">
            Waiting for a scan…
          </div>
        )}
      </div>
      {lookup && onConfirm ? (
        // Outside the live region, and never triggered by the handheld field's Enter: a deliberate click
        // (or Space/Enter while this button has focus) confirms.
        <Button
          size="lg"
          className={desk ? 'h-14 self-start px-6 text-[0.9375rem]' : 'h-12 w-full text-[0.9375rem]'}
          loading={confirming}
          aria-label={`Check in ${lookup.name}`}
          onClick={() => onConfirm(lookup.attendeeId)}
        >
          Check in
        </Button>
      ) : null}
    </div>
  )
}

function ResultBody({ outcome, desk }: { outcome: ScanOutcome; desk: boolean }) {
  const view = describeOutcome(outcome, desk ? 'desk' : 'phone')
  return (
    <div
      className={cn(
        'flex ring-1',
        surfaces[view.tone],
        desk ? 'min-h-40 items-center gap-5 rounded-lg p-6' : 'items-start gap-3.5 rounded-2xl px-5 py-4',
      )}
    >
      <span
        aria-hidden="true"
        className={cn('flex flex-none items-center justify-center rounded-full', iconCircles[view.tone], desk ? 'size-18' : 'size-11')}
      >
        <ToneIcon tone={view.tone} size={desk ? 36 : 22} />
      </span>
      <div className="flex min-w-0 flex-col gap-0.5">
        <span className={cn('font-bold', desk ? 'text-[1.75rem] leading-tight tracking-tight' : 'text-lg')}>{view.title}</span>
        {view.name ? <span className={cn('font-semibold', desk ? 'text-xl' : 'text-[0.9375rem]')}>{view.name}</span> : null}
        <span className={desk ? 'text-[0.9375rem]' : 'text-[0.8125rem]'}>{view.meta}</span>
      </div>
    </div>
  )
}
