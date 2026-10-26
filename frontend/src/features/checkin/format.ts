import { EVENT_TIME_ZONE } from '@/features/events/format'
import { formatPeso } from '@/lib/format'
import type { ScanResponse, ScanResultKind } from './schemas'

const timeFmt = new Intl.DateTimeFormat('en-PH', { timeZone: EVENT_TIME_ZONE, hour: 'numeric', minute: '2-digit' })

/** "8:14 AM" in Manila. */
export function formatScanTime(iso: string | null | undefined): string {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : timeFmt.format(d)
}

/** "now", "4 min", or the time of day once it is an hour old. */
export function formatAgo(iso: string, now = Date.now()): string {
  const t = new Date(iso).getTime()
  if (Number.isNaN(t)) return ''
  const minutes = Math.floor((now - t) / 60_000)
  if (minutes < 1) return 'now'
  if (minutes < 60) return `${minutes} min`
  return formatScanTime(iso)
}

export type ResultTone = 'green' | 'yellow' | 'red' | 'blue' | 'gray'

/** What happened on the last scan, as shown in the live result card. */
export type ScanOutcome =
  | { kind: 'result'; data: ScanResponse }
  | { kind: 'slow' }
  | { kind: 'error'; message: string }

export type ResultView = { tone: ResultTone; title: string; name?: string; meta: string }

/** A Lookup that staff may confirm (only a paid attendee who isn't in yet). */
export function confirmableLookup(outcome: ScanOutcome | null): { attendeeId: string; name: string } | null {
  if (outcome?.kind !== 'result') return null
  const d = outcome.data
  if (d.result !== 'Lookup' || d.status !== 'Paid' || !d.attendeeId) return null
  return { attendeeId: d.attendeeId, name: d.attendee?.name ?? 'this attendee' }
}

/**
 * Search rules (same as the API): names need 3+ characters; participant IDs match in full or by a
 * prefix of 6+ characters. Names can't contain digits, so a digit or "HTN-" means an ID.
 */
export function searchTermReady(term: string): boolean {
  const t = term.trim()
  const idLike = /\d/.test(t) || /^htn-/i.test(t)
  return t.length >= (idLike ? 6 : 3)
}

export const SEARCH_HINT = 'Type at least 3 letters of a name, or at least 6 characters of a participant ID (e.g. HTN-7K).'

export const SLOW_DOWN_TITLE = 'Slow down'
export const SLOW_DOWN_MESSAGE = 'Too many scans in a short time. Wait a few seconds, then scan again.'

/** Card copy per result (canvas frames 12 and 12b). Desk cards carry a little more guidance. */
export function describeOutcome(outcome: ScanOutcome, size: 'desk' | 'phone'): ResultView {
  const desk = size === 'desk'
  if (outcome.kind === 'slow') return { tone: 'yellow', title: SLOW_DOWN_TITLE, meta: SLOW_DOWN_MESSAGE }
  if (outcome.kind === 'error') return { tone: 'red', title: 'Scan not sent', meta: outcome.message }

  const { result, attendee, checkedInAt, checkedInStation, amountDue } = outcome.data
  const name = attendee?.name
  switch (result) {
    case 'Welcome': {
      const parts: string[] = []
      if (attendee) {
        parts.push(desk ? `${attendee.ticketName} ticket` : attendee.ticketName, attendee.participantId)
        if (attendee.shirtSize) parts.push(desk ? `shirt ${attendee.shirtSize} (for sizing)` : `Shirt ${attendee.shirtSize}`)
        if (desk) parts.push(attendee.isMember ? 'member' : 'not a member yet')
      }
      return { tone: 'green', title: 'Welcome in', name, meta: parts.join(' · ') }
    }
    case 'AlreadyCheckedIn': {
      const time = formatScanTime(checkedInAt)
      const when = [time ? `Scanned ${time}` : 'Scanned earlier', checkedInStation ? `at ${checkedInStation}` : ''].filter(Boolean).join(' ')
      return {
        tone: 'yellow',
        title: 'Already checked in',
        name,
        meta: desk ? `${when} · check their ID if they say it is their first entry` : when,
      }
    }
    case 'NotPaid': {
      const settle = amountDue != null ? `Send to the help desk to settle ${formatPeso(amountDue)}` : 'Send to the help desk to settle the payment'
      return { tone: 'red', title: 'Not paid', name, meta: desk ? `${settle} (GCash or bank)` : settle }
    }
    case 'Cancelled':
      return { tone: 'red', title: 'Cancelled', name, meta: 'This registration was cancelled. Send them to the help desk.' }
    case 'Lookup': {
      const details = attendee ? [attendee.ticketName, attendee.participantId, attendee.shirtSize ? `Shirt ${attendee.shirtSize}` : ''].filter(Boolean).join(' · ') : ''
      const time = formatScanTime(checkedInAt)
      const state =
        outcome.data.status === 'Paid'
          ? 'Paid · check their ID, then confirm the check-in'
          : outcome.data.status === 'CheckedIn'
            ? `Already checked in · ${[time ? `Scanned ${time}` : 'Scanned earlier', checkedInStation ? `at ${checkedInStation}` : ''].filter(Boolean).join(' ')}`
            : outcome.data.status === 'PendingPayment'
              ? `Not paid · ${amountDue != null ? `Send to the help desk to settle ${formatPeso(amountDue)}` : 'Send to the help desk to settle the payment'}`
              : outcome.data.status === 'Cancelled'
                ? 'Cancelled · This registration was cancelled. Send them to the help desk.'
                : ''
      return { tone: 'blue', title: 'Check the ticket holder', name, meta: [details, state].filter(Boolean).join(' — ') }
    }
    case 'Invalid':
      return {
        tone: 'red',
        title: 'Not a valid ticket',
        name: 'Unknown code',
        meta: desk
          ? 'The QR signature does not match. Ask for the ticket email or search their name.'
          : 'Ask for the ticket email or search their name.',
      }
  }
}

/** Short labels for the recent scans list (always shown next to the color dot). */
export const recentLabels: Record<ScanResultKind, string> = {
  Welcome: 'Checked in',
  AlreadyCheckedIn: 'Already in',
  NotPaid: 'Not paid',
  Cancelled: 'Cancelled',
  Invalid: 'Invalid',
  Lookup: 'Looked up',
}

export const recentTones: Record<ScanResultKind, ResultTone> = {
  Welcome: 'green',
  AlreadyCheckedIn: 'yellow',
  NotPaid: 'red',
  Cancelled: 'red',
  Invalid: 'red',
  Lookup: 'blue',
}

export function percentCheckedIn(checkedIn: number, paid: number): number {
  if (paid <= 0) return 0
  return Math.min(100, Math.round((checkedIn / paid) * 100))
}
