import { useState, type ReactNode } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Drawer } from '@/components/ui/Drawer'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import { formatEventDateTime } from '@/features/events/format'
import { formatPeso } from '@/lib/format'
import { useCancelRegistrationMutation, useRegistrationQuery, useResendBillingMutation, useResendTicketMutation, useRevertPaymentMutation } from '../api'
import { buyerLabel, fullName, historyText, methodLabels } from '../format'
import type { RegistrationDetail, RegistrationRow } from '../schemas'
import { RegistrationStatusBadges } from './badges'
import { ReasonDialog } from './ReasonDialog'

function Section({ title, children }: { title: string; children: ReactNode }) {
  const id = `reg-${title.toLowerCase().replace(/\W+/g, '-')}`
  return (
    <section aria-labelledby={id} className="flex flex-col gap-2 border-t border-gray-100 pt-4 text-[0.8125rem] dark:border-white/10">
      <h3 id={id} className="font-semibold text-gray-700 dark:text-gray-300">
        {title}
      </h3>
      {children}
    </section>
  )
}

function Row({ label, children, strong }: { label: ReactNode; children: ReactNode; strong?: boolean }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-gray-600 dark:text-gray-400">{label}</dt>
      <dd className={strong ? 'font-semibold text-gray-900 tabular-nums dark:text-gray-50' : 'text-right text-gray-900 tabular-nums dark:text-gray-50'}>{children}</dd>
    </div>
  )
}

type Modal = 'cancel' | 'revert' | null

/** Registration details (canvas frame 09 row drawer): attendees, totals, proof, payment, history and actions. */
export function RegistrationDrawer({
  eventId,
  row,
  onClose,
  onMarkPaid,
}: {
  eventId: string
  row: RegistrationRow
  onClose: () => void
  onMarkPaid: (r: RegistrationRow) => void
}) {
  const { toast } = useToast()
  const query = useRegistrationQuery(eventId, row.id)
  const cancel = useCancelRegistrationMutation(eventId)
  const revert = useRevertPaymentMutation(eventId)
  const resendBilling = useResendBillingMutation(eventId)
  const resendTicket = useResendTicketMutation(eventId)
  const [modal, setModal] = useState<Modal>(null)

  const r: RegistrationRow | RegistrationDetail = query.data ?? row
  const name = buyerLabel(r)

  function onCancel(reason: string) {
    cancel.mutate(
      { id: r.id, reason },
      {
        onSuccess: () => {
          setModal(null)
          toast({ tone: 'success', title: `${r.buyerName}'s registration was cancelled.`, description: 'The seat and any voucher use are freed.' })
        },
        onError: (e) => {
          setModal(null)
          toast({ tone: 'error', title: 'Could not cancel the registration.', description: errorMessage(e) })
        },
      },
    )
  }

  function onRevert(reason: string) {
    revert.mutate(
      { id: r.id, reason },
      {
        onSuccess: () => {
          setModal(null)
          toast({ tone: 'success', title: 'Payment reverted.', description: 'The registration is pending payment again and the old QR code no longer works.' })
        },
        onError: (e) => {
          setModal(null)
          toast({ tone: 'error', title: 'Could not revert the payment.', description: errorMessage(e) })
        },
      },
    )
  }

  function onResendBilling() {
    resendBilling.mutate([r.id], {
      onSuccess: () => toast({ tone: 'success', title: `Billing email resent to ${r.buyerEmail}.` }),
      onError: (e) => toast({ tone: 'error', title: 'Could not resend the billing email.', description: errorMessage(e) }),
    })
  }

  function onResendTicket() {
    resendTicket.mutate(r.id, {
      onSuccess: () => toast({ tone: 'success', title: r.attendeeCount > 1 ? 'Tickets resent to the group.' : `Ticket resent to ${r.buyerEmail}.` }),
      onError: (e) => toast({ tone: 'error', title: 'Could not resend the ticket.', description: errorMessage(e) }),
    })
  }

  const footer =
    r.status === 'Cancelled' ? undefined : (
      <>
        {r.status === 'PendingPayment' ? (
          <>
            <Button onClick={() => onMarkPaid(r)}>Mark as paid & send ticket</Button>
            <Button variant="secondary" onClick={onResendBilling} loading={resendBilling.isPending}>
              Resend billing email
            </Button>
          </>
        ) : (
          <Button variant="secondary" onClick={onResendTicket} loading={resendTicket.isPending}>
            Resend ticket
          </Button>
        )}
        {r.status === 'Paid' ? (
          <Button variant="danger" onClick={() => setModal('revert')}>
            Revert payment
          </Button>
        ) : null}
        {r.status === 'PendingPayment' || r.status === 'Paid' ? (
          <Button variant="danger" onClick={() => setModal('cancel')}>
            Cancel registration
          </Button>
        ) : null}
      </>
    )

  return (
    <>
      <Drawer open onClose={onClose} label="Registration details" footer={footer}>
        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <p className="text-lg font-semibold text-gray-900 dark:text-gray-50">{name}</p>
            <p className="truncate text-[0.8125rem] text-gray-600 dark:text-gray-400">{r.buyerEmail}</p>
            <RegistrationStatusBadges status={r.status} overdue={r.overdue} />
            <p className="text-xs text-gray-600 dark:text-gray-400">
              {r.ticketName} · registered {formatEventDateTime(r.createdAt)} · {r.source === 'Manual' ? 'added by an admin' : 'public page'}
            </p>
          </div>

          {query.isPending ? (
            <div role="status" aria-label="Loading registration" className="flex flex-col gap-2">
              <Skeleton className="h-4 w-56" />
              <Skeleton className="h-4 w-44" />
              <Skeleton className="h-32 w-full" />
            </div>
          ) : query.isError ? (
            <Alert
              tone="red"
              live="alert"
              action={
                <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
                  Try again
                </Button>
              }
            >
              Could not load the registration. {errorMessage(query.error)}
            </Alert>
          ) : (
            <DetailSections d={query.data} />
          )}
        </div>
      </Drawer>

      {modal === 'cancel' ? (
        <ReasonDialog
          title={`Cancel ${r.buyerName}'s registration?`}
          description="The seat and any voucher use are freed. Nothing is deleted; the registration stays in the list as Cancelled."
          confirmLabel="Cancel registration"
          placeholder="e.g. Asked to cancel by email"
          pending={cancel.isPending}
          onConfirm={onCancel}
          onClose={() => setModal(null)}
        />
      ) : null}
      {modal === 'revert' ? (
        <ReasonDialog
          title="Revert this payment?"
          description="The registration goes back to Pending payment and the ticket QR code stops working. Use this when a payment was marked by mistake."
          confirmLabel="Revert payment"
          placeholder="e.g. Reference didn't match the bank statement"
          pending={revert.isPending}
          onConfirm={onRevert}
          onClose={() => setModal(null)}
        />
      ) : null}
    </>
  )
}

function DetailSections({ d }: { d: RegistrationDetail }) {
  return (
    <>
      <Section title={d.attendees.length > 1 ? `Attendees (${d.attendees.length})` : 'Attendee'}>
        <ul className="flex flex-col gap-2.5">
          {d.attendees.map((a) => (
            <li key={a.id} className="flex flex-col gap-0.5 rounded-md p-3 ring-1 ring-gray-200 dark:ring-white/10">
              <span className="font-medium text-gray-900 dark:text-gray-50">{fullName(a)}</span>
              <span className="font-mono text-xs text-gray-700 dark:text-gray-300">{a.participantId}</span>
              {a.email ? <span className="text-gray-600 dark:text-gray-400">{a.email}</span> : null}
              {a.company || a.shirtSize ? (
                <span className="text-gray-600 dark:text-gray-400">{[a.company, a.shirtSize ? `Shirt ${a.shirtSize}` : null].filter(Boolean).join(' · ')}</span>
              ) : null}
              {a.checkedInAt ? <span className="text-blue-700 dark:text-blue-300">Checked in {formatEventDateTime(a.checkedInAt)}</span> : null}
            </li>
          ))}
        </ul>
      </Section>

      <Section title="Totals">
        <dl className="flex flex-col gap-1.5">
          <Row label={d.ticketName}>{formatPeso(d.subtotal)}</Row>
          {d.voucherCode || d.discount > 0 ? (
            <Row label={`Voucher${d.voucherCode ? ` ${d.voucherCode}` : ''}`}>−{formatPeso(d.discount)}</Row>
          ) : null}
          <Row label="Total" strong>
            {formatPeso(d.total)}
          </Row>
          {d.status === 'PendingPayment' && d.deadline ? <Row label="Pay before">{formatEventDateTime(d.deadline)}</Row> : null}
        </dl>
      </Section>

      <Section title="Proof of payment">
        {d.proof ? (
          <div className="flex flex-col gap-2">
            <a href={d.proof.url} target="_blank" rel="noopener noreferrer" className="block w-fit rounded-md ring-1 ring-gray-200 dark:ring-white/10">
              <img src={d.proof.url} alt={`Proof of payment uploaded by ${d.buyerName}`} className="max-h-72 rounded-md object-contain" />
              <span className="sr-only"> (opens full size in a new tab)</span>
            </a>
            <dl className="flex flex-col gap-1.5">
              {d.proof.reference ? (
                <Row label="Reference">
                  <span className="font-mono">{d.proof.reference}</span>
                </Row>
              ) : null}
              <Row label="Uploaded">{formatEventDateTime(d.proof.uploadedAt)}</Row>
            </dl>
          </div>
        ) : (
          <p className="text-gray-600 dark:text-gray-400">No proof uploaded yet.</p>
        )}
      </Section>

      <Section title="Payment">
        {d.payment ? (
          <dl className="flex flex-col gap-1.5">
            <Row label="Method">
              {methodLabels[d.payment.method]}
              {d.payment.paymentAccountLabel ? ` · ${d.payment.paymentAccountLabel}` : ''}
            </Row>
            {d.payment.reference ? (
              <Row label="Reference">
                <span className="font-mono">{d.payment.reference}</span>
              </Row>
            ) : null}
            <Row label="Amount">{formatPeso(d.payment.amount)}</Row>
            {d.payment.receivedAt ? <Row label="Received">{formatEventDateTime(d.payment.receivedAt)}</Row> : null}
            {d.payment.markedByName ? <Row label="Marked by">{d.payment.markedByName}</Row> : null}
          </dl>
        ) : (
          <p className="text-gray-600 dark:text-gray-400">{d.status === 'Cancelled' ? 'No payment recorded.' : 'Not paid yet.'}</p>
        )}
      </Section>

      <Section title="History">
        {d.history.length === 0 ? (
          <p className="text-gray-600 dark:text-gray-400">No changes recorded yet.</p>
        ) : (
          <ol className="flex flex-col gap-2">
            {d.history.map((h, i) => (
              <li key={`${h.at}-${i}`} className="flex gap-2 text-gray-700 dark:text-gray-300">
                <span className="min-w-[120px] shrink-0 text-gray-600 tabular-nums dark:text-gray-400">{formatEventDateTime(h.at)}</span>
                <span className="flex flex-col">
                  <span>
                    {historyText(h)}
                    {h.actorName ? <span className="text-gray-600 dark:text-gray-400"> · by {h.actorName}</span> : null}
                  </span>
                  {h.reason ? <span className="text-gray-600 dark:text-gray-400">Reason: {h.reason}</span> : null}
                </span>
              </li>
            ))}
          </ol>
        )}
      </Section>
    </>
  )
}
