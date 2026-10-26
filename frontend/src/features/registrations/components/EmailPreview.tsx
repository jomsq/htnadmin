import { useState } from 'react'
import { QrCode } from '@/components/ui/QrCode'
import { formatEventDateTime } from '@/features/events/format'
import type { ProofMode } from '@/features/events/schemas'
import type { PaymentAccount } from '@/features/settings/schemas'
import { formatPeso } from '@/lib/format'
import { SAMPLE } from '../format'
import type { EmailTemplate } from '../schemas'

export type EmailPreviewData = {
  template: EmailTemplate
  accent: string
  logoUrl: string | null
  eventName: string
  startsAt: string
  venue: string
  ticketName: string
  amount: number
  proofMode: ProofMode
  deadlineHours: number
  accounts: PaymentAccount[]
}

function accountHeading(a: PaymentAccount): string {
  if (a.kind === 'GCash') return 'GCASH'
  if (a.kind === 'Bank') return `BANK TRANSFER${a.bankName ? ` · ${a.bankName.toUpperCase()}` : ''}`
  return 'CASH'
}

/**
 * Live preview of the fixed billing / ticket layout (canvas frame 11). Only the accent color, logo,
 * payment accounts and deadline change. Emails are always light, so the preview stays light in dark mode.
 */
export function EmailPreview(d: EmailPreviewData) {
  // Sample registration time: when the preview opened.
  const [now] = useState(() => Date.now())
  const deadline = new Date(now + d.deadlineHours * 3_600_000).toISOString()
  return (
    <div className="flex justify-center rounded-xl bg-gray-100 px-4 py-8 dark:bg-white/5">
      <article
        aria-label={`${d.template === 'billing' ? 'Billing' : 'Ticket'} email preview`}
        className="w-full max-w-[560px] overflow-hidden rounded-xl bg-white text-gray-700 shadow-sm ring-1 ring-gray-900/5"
      >
        <div className="flex items-center gap-3 px-8 py-6" style={{ backgroundColor: d.accent }}>
          <div className="flex size-9 items-center justify-center overflow-hidden rounded-md bg-white text-[0.6875rem] font-bold text-gray-900">
            {d.logoUrl ? <img src={d.logoUrl} alt="" className="max-h-7 max-w-7 object-contain" /> : <span aria-hidden="true">HTN</span>}
          </div>
          <span className="text-base font-semibold text-white">{d.eventName}</span>
        </div>

        {d.template === 'billing' ? (
          <div className="flex flex-col gap-5 p-8 text-[0.9375rem] leading-relaxed">
            <h3 className="text-[1.375rem] font-semibold text-gray-900">Complete your payment, {SAMPLE.firstName}</h3>
            <p>
              Thanks for registering. Your seat is held for {d.deadlineHours} hours. Send <strong className="text-gray-900">{formatPeso(d.amount)}</strong>{' '}
              with reference <strong className="font-mono text-gray-900">{SAMPLE.participantId}</strong> in the message.
            </p>
            {d.accounts.length ? (
              <div className="grid gap-3 sm:grid-cols-2">
                {d.accounts.map((a) => (
                  <div key={a.id} className="flex flex-col gap-1 rounded-md p-3.5 shadow-xs ring-1 ring-gray-900/10">
                    <span className="text-xs font-semibold" style={{ color: d.accent }}>
                      {accountHeading(a)}
                    </span>
                    <span className="text-sm font-medium text-gray-900">{a.accountName}</span>
                    <span className="font-mono text-sm">{a.accountNumber ?? a.accountNumberMasked ?? ''}</span>
                  </div>
                ))}
              </div>
            ) : (
              <p className="rounded-md bg-yellow-50 p-3 text-sm text-yellow-800">No payment account selected yet.</p>
            )}
            <div className="flex flex-col gap-1.5 rounded-lg bg-gray-50 p-3.5 text-sm">
              <div className="flex justify-between gap-4">
                <span>{d.ticketName} ticket × 1</span>
                <span className="font-semibold text-gray-900 tabular-nums">{formatPeso(d.amount)}</span>
              </div>
              <div className="flex justify-between gap-4">
                <span>Pay before</span>
                <span className="font-semibold text-gray-900">{formatEventDateTime(deadline)}</span>
              </div>
            </div>
            {d.proofMode === 'Upload' ? (
              <span className="inline-flex h-11 w-fit items-center rounded-md px-5 text-[0.9375rem] font-semibold text-white" style={{ backgroundColor: d.accent }}>
                Upload proof of payment
              </span>
            ) : (
              <p className="text-sm">
                <strong className="text-gray-900">Reply to this email</strong> with a screenshot or photo of your payment.
              </p>
            )}
            <p className="text-[0.8125rem] text-gray-600">Your ticket and QR code arrive once our team confirms payment.</p>
          </div>
        ) : (
          <div className="flex flex-col items-center gap-5 p-8 text-center text-[0.9375rem] leading-relaxed">
            <h3 className="text-[1.375rem] font-semibold text-gray-900">You&apos;re in, {SAMPLE.firstName}</h3>
            <p>Payment received. Show this QR code at the entrance.</p>
            <QrCode value={`${SAMPLE.participantId}-preview`} label={`Sample ticket QR code for ${SAMPLE.participantId}`} size={176} />
            <dl className="grid w-full grid-cols-2 gap-3 border-t border-dashed border-gray-300 pt-4 text-left text-sm">
              <div className="flex flex-col">
                <dt className="text-xs text-gray-600">Attendee</dt>
                <dd className="font-semibold text-gray-900">{SAMPLE.name}</dd>
              </div>
              <div className="flex flex-col">
                <dt className="text-xs text-gray-600">Ticket</dt>
                <dd className="font-semibold text-gray-900">
                  {d.ticketName} · <span className="font-mono">{SAMPLE.participantId}</span>
                </dd>
              </div>
              <div className="flex flex-col">
                <dt className="text-xs text-gray-600">When</dt>
                <dd className="font-semibold text-gray-900">{formatEventDateTime(d.startsAt)}</dd>
              </div>
              <div className="flex flex-col">
                <dt className="text-xs text-gray-600">Where</dt>
                <dd className="font-semibold text-gray-900">{d.venue}</dd>
              </div>
            </dl>
            <span className="inline-flex h-11 items-center rounded-md px-5 text-[0.9375rem] font-semibold text-white" style={{ backgroundColor: d.accent }}>
              Add to calendar
            </span>
          </div>
        )}

        <div className="bg-gray-50 px-8 py-4 text-center text-xs text-gray-600">HackTheNorth.PH Inc. · a non-stock, non-profit corporation</div>
      </article>
    </div>
  )
}
