import { useEffect, useRef } from 'react'
import { Button } from '@/components/ui/Button'
import { cardClass } from '@/components/ui/Card'
import { CheckIcon } from '@/components/ui/icons'
import { formatEventDateTime } from '@/features/events/format'
import { cn } from '@/lib/cn'
import { formatPeso } from '@/lib/format'
import type { RegistrationResult } from '../schemas'

/** "You're registered" (canvas frame 10, submitted state). Shows only what the registrant typed or needs to pay. */
export function RegistrationSuccess({ result, onAgain }: { result: RegistrationResult; onAgain: () => void }) {
  const headingRef = useRef<HTMLHeadingElement>(null)
  useEffect(() => {
    headingRef.current?.focus()
    window.scrollTo?.({ top: 0 })
  }, [])

  const free = result.amountDue === 0
  const many = result.participantIds.length > 1

  return (
    <section aria-labelledby="registered-heading" className="flex flex-col items-center gap-4 px-5 py-10 text-center">
      <div aria-hidden="true" className="flex size-14 items-center justify-center rounded-full bg-green-50 text-green-700 dark:bg-green-500/10 dark:text-green-400">
        <CheckIcon size={28} />
      </div>
      <h2 id="registered-heading" ref={headingRef} tabIndex={-1} className="text-xl font-semibold text-gray-900 outline-none dark:text-gray-50">
        You&apos;re registered
      </h2>
      <p role="status" className="text-[0.9375rem] leading-relaxed text-gray-600 dark:text-gray-400">
        {free ? (
          <>
            Your {many ? 'tickets' : 'ticket'} with QR code {many ? 'are' : 'is'} on the way to <strong className="text-gray-900 dark:text-gray-50">{result.email}</strong>.
          </>
        ) : (
          <>
            We sent the billing details to <strong className="text-gray-900 dark:text-gray-50">{result.email}</strong>. Pay via GCash or bank transfer,{' '}
            {result.proofMode === 'Upload'
              ? 'then upload your proof of payment using the link in that email.'
              : 'then reply to the billing email with your proof of payment.'}{' '}
            Your ticket with QR code arrives once we confirm.
          </>
        )}
      </p>

      <dl className={cn(cardClass, 'flex w-full flex-col gap-2 p-4 text-left text-sm')}>
        <div className="flex justify-between gap-4">
          <dt className="text-gray-600 dark:text-gray-400">{many ? 'Participant IDs' : 'Participant ID'}</dt>
          <dd className="flex flex-col items-end font-mono font-semibold text-gray-900 dark:text-gray-50">
            {result.participantIds.map((id) => (
              <span key={id}>{id}</span>
            ))}
          </dd>
        </div>
        <div className="flex justify-between gap-4">
          <dt className="text-gray-600 dark:text-gray-400">Amount due</dt>
          <dd className="font-semibold text-gray-900 tabular-nums dark:text-gray-50">{formatPeso(result.amountDue)}</dd>
        </div>
        {!free && result.deadline ? (
          <div className="flex justify-between gap-4">
            <dt className="text-gray-600 dark:text-gray-400">Pay before</dt>
            <dd className="font-semibold text-gray-900 dark:text-gray-50">{formatEventDateTime(result.deadline)}</dd>
          </div>
        ) : null}
      </dl>
      {!free ? (
        <p className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
          Use {many ? 'the first participant ID' : 'your participant ID'} as the reference when you pay.
        </p>
      ) : null}

      <Button variant="secondary" size="lg" onClick={onAgain}>
        Register someone else
      </Button>
    </section>
  )
}
