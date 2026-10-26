/** API fixtures shaped like phase3-contract.md section 3C. */

export const attendee = (over: Record<string, unknown> = {}) => ({
  name: 'Maria Santos',
  ticketName: 'Regular',
  participantId: 'HTN-7K3Q-9XWD',
  shirtSize: 'M',
  isMember: false,
  ...over,
})

export const welcome = (over: Record<string, unknown> = {}) => ({
  result: 'Welcome',
  attendee: attendee(),
  checkedInAt: '2026-11-22T01:02:00Z',
  checkedInStation: 'Main entrance',
  amountDue: null,
  ...over,
})

export const alreadyIn = () => ({
  result: 'AlreadyCheckedIn',
  attendee: attendee({ name: 'Hannah Lee', participantId: 'HTN-9TCW-KE6P' }),
  // 8:14 AM in Manila
  checkedInAt: '2026-11-22T00:14:00Z',
  checkedInStation: 'Desk 1',
  amountDue: null,
})

export const notPaid = () => ({
  result: 'NotPaid',
  attendee: attendee({ name: 'Juan Dela Cruz', participantId: 'HTN-M2PA-4RCE' }),
  checkedInAt: null,
  checkedInStation: null,
  amountDue: 1500,
})

export const cancelled = () => ({
  result: 'Cancelled',
  attendee: attendee({ name: 'Rina Cruz', participantId: 'HTN-R4YE-QJ5M' }),
  checkedInAt: null,
  checkedInStation: null,
  amountDue: null,
})

/** A typed or QR'd participant ID: staff must confirm before anyone is checked in. */
export const lookup = (status: 'PendingPayment' | 'Paid' | 'CheckedIn' | 'Cancelled', over: Record<string, unknown> = {}) => ({
  result: 'Lookup',
  attendeeId: 'a1',
  attendee: attendee(),
  status,
  checkedInAt: status === 'CheckedIn' ? '2026-11-22T00:14:00Z' : null,
  checkedInStation: status === 'CheckedIn' ? 'Desk 1' : null,
  amountDue: status === 'PendingPayment' ? 1500 : null,
  ...over,
})

export const invalid = () => ({ result: 'Invalid', attendee: null, checkedInAt: null, checkedInStation: null, amountDue: null })

export const stats = (over: Record<string, unknown> = {}) => ({ checkedIn: 187, paid: 264, thisStation: 41, stationsOnline: 4, ...over })

export const recent = () => [
  { name: 'Paolo Garcia', participantId: 'HTN-JD4R-2MQY', result: 'Welcome', at: new Date(Date.now() - 2 * 60_000).toISOString() },
  { name: 'Hannah Lee', participantId: 'HTN-9TCW-KE6P', result: 'AlreadyCheckedIn', at: new Date(Date.now() - 4 * 60_000).toISOString() },
]

export const searchRows = () => [
  { attendeeId: 'a1', name: 'Maria Santos', participantId: 'HTN-7K3Q-9XWD', ticketName: 'Regular', status: 'Paid' },
  { attendeeId: 'a2', name: 'Mario Reyes', participantId: 'HTN-5HXF-W7GB', ticketName: 'Student', status: 'PendingPayment' },
]
