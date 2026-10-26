import { toManilaInput } from '@/features/events/format'
import type { EmailTemplate, PaymentMethod, RegistrationHistoryEntry, RegistrationRow, RegistrationStatus } from './schemas'

export const statusLabels: Record<RegistrationStatus, string> = {
  PendingPayment: 'Pending payment',
  Paid: 'Paid',
  CheckedIn: 'Checked in',
  Cancelled: 'Cancelled',
}

export const methodLabels: Record<PaymentMethod, string> = {
  GCash: 'GCash',
  Bank: 'Bank transfer',
  Cash: 'Cash',
  Comp: 'Comped',
}

/** Two lines for the Payment column: what was used, and the reference (or where the proof stands). */
export function paymentCell(r: Pick<RegistrationRow, 'method' | 'reference' | 'hasProof' | 'status'>): { primary: string; secondary: string } {
  if (r.method) {
    return { primary: methodLabels[r.method], secondary: r.reference ? `Ref ${r.reference}` : '—' }
  }
  if (r.status === 'Cancelled') return { primary: '—', secondary: '' }
  if (r.hasProof) return { primary: 'Proof uploaded', secondary: r.reference ? `Ref ${r.reference}` : '' }
  return { primary: 'Awaiting proof', secondary: '' }
}

/** "Maria Santos" or "Acme Dev Team (5)" for group checkouts. */
export function buyerLabel(r: Pick<RegistrationRow, 'buyerName' | 'attendeeCount'>): string {
  return r.attendeeCount > 1 ? `${r.buyerName} (${r.attendeeCount})` : r.buyerName
}

const historyLabels: Record<string, string> = {
  Registered: 'Registered',
  Created: 'Registered',
  ManualRegistered: 'Added manually',
  BillingSent: 'Billing email sent',
  BillingResent: 'Billing email resent',
  ProofUploaded: 'Proof of payment uploaded',
  Paid: 'Marked as paid',
  MarkedPaid: 'Marked as paid',
  TicketSent: 'Ticket email sent',
  TicketResent: 'Ticket email resent',
  PaymentReverted: 'Payment reverted',
  Reverted: 'Payment reverted',
  Cancelled: 'Cancelled',
  CheckedIn: 'Checked in',
}

export function historyText(h: RegistrationHistoryEntry): string {
  return historyLabels[h.kind] ?? h.kind.replace(/([a-z])([A-Z])/g, '$1 $2')
}

/** The current time as a datetime-local value in Manila (default "received at"). */
export function nowManilaInput(): string {
  return toManilaInput(new Date().toISOString())
}

export function fullName(a: { firstName: string; middleName?: string | null; lastName: string }): string {
  return [a.firstName, a.middleName, a.lastName].filter((p) => p && p.trim()).join(' ')
}

/** Sample attendee for previews (the canvas uses the same one). */
export const SAMPLE = { firstName: 'Maria', name: 'Maria Santos', email: 'maria.santos@gmail.com', participantId: 'HTN-7K3Q-9XWD' }

export function emailSubject(template: EmailTemplate, eventName: string): string {
  return template === 'billing' ? `Complete your payment for ${eventName}` : `Your ${eventName} ticket`
}
