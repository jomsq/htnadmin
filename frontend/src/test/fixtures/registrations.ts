/** API fixtures shaped like phase3-contract.md section 3B. */

export const row = (over: Record<string, unknown> = {}) => ({
  id: 'r1',
  buyerName: 'Maria Santos',
  buyerEmail: 'maria.santos@gmail.com',
  attendeeCount: 1,
  participantIds: ['HTN-7K3Q-9XWD'],
  ticketName: 'Regular',
  total: 1500,
  status: 'PendingPayment',
  overdue: false,
  method: null,
  reference: null,
  proofReference: null,
  hasProof: true,
  createdAt: '2026-10-25T02:00:00Z',
  source: 'Public',
  ...over,
})

export const rows = [
  row({ proofReference: '7741 0923' }),
  row({ id: 'r2', buyerName: 'Juan Dela Cruz', buyerEmail: 'jdc@company.ph', participantIds: ['HTN-M2PA-4RCE'], hasProof: false, overdue: true }),
  row({
    id: 'r3',
    buyerName: 'Acme Dev Team',
    buyerEmail: 'lead@acmedev.ph',
    attendeeCount: 5,
    participantIds: ['HTN-Q8VN-T3ZK', 'HTN-Q8VN-T3Z2', 'HTN-Q8VN-T3Z3', 'HTN-Q8VN-T3Z4', 'HTN-Q8VN-T3Z5'],
    ticketName: 'Team of 5',
    total: 6000,
  }),
  row({ id: 'r4', buyerName: 'Carlo Mendoza', buyerEmail: 'carlo@startup.io', participantIds: ['HTN-JD4R-2MQY'], status: 'Paid', method: 'GCash', reference: '5512 0034', hasProof: false }),
]

export const counts = { all: 312, pendingPayment: 48, paid: 240, checkedIn: 20, cancelled: 4, overdue: 6 }

export const list = (items: unknown[] = rows, over: Record<string, unknown> = {}) => ({ items, total: items.length, page: 1, pageSize: 25, counts, ...over })

export const registrationDetail = (over: Record<string, unknown> = {}) => ({
  ...row(),
  attendees: [
    {
      id: 'a1',
      participantId: 'HTN-7K3Q-9XWD',
      firstName: 'Maria',
      middleName: null,
      lastName: 'Santos',
      email: 'maria.santos@gmail.com',
      mobile: '+639171234567',
      gender: 'Female',
      occupation: 'Developer',
      company: 'Acme',
      shirtSize: 'M',
      hearAbout: 'Facebook',
      checkedInAt: null,
    },
  ],
  subtotal: 1500,
  discount: 0,
  voucherCode: null,
  deadline: '2026-10-27T02:00:00Z',
  payment: null,
  proof: { reference: '7741 0923', uploadedAt: '2026-10-25T05:00:00Z', url: '/api/events/e1/registrations/r1/proof' },
  history: [{ at: '2026-10-25T02:00:00Z', kind: 'Registered', actorName: null, reason: null }],
  ...over,
})

export const paymentAccounts = [
  {
    id: 'a1',
    kind: 'GCash',
    label: 'GCash — HTN',
    bankName: null,
    accountName: 'HackTheNorth.PH Inc.',
    accountNumber: '0917 123 4567',
    accountNumberMasked: '•••• 4567',
    isDefault: true,
    isActive: true,
  },
  {
    id: 'a2',
    kind: 'Bank',
    label: 'BPI — HTN',
    bankName: 'BPI',
    accountName: 'HackTheNorth.PH Inc.',
    accountNumber: '1234 5678 90',
    accountNumberMasked: '•••• 7890',
    isDefault: false,
    isActive: true,
  },
]

export const publicEvent = (over: Record<string, unknown> = {}) => ({
  name: 'HTN Con 2026',
  startsAt: '2026-11-22T00:00:00Z',
  endsAt: '2026-11-22T10:00:00Z',
  venue: 'SMX Convention Center, Pasay',
  description: 'The yearly HackTheNorth.PH conference.',
  coverUrl: null,
  logoUrl: null,
  brandColor: '#B71C1C',
  groupRegistration: true,
  proofMode: 'Upload',
  tickets: [
    { id: 't1', name: 'Regular', price: 1500, groupSize: 1, remaining: 79, note: null },
    { id: 't2', name: 'Student', price: 800, groupSize: 1, remaining: 0, note: 'Valid school ID at entry' },
    { id: 't3', name: 'Team of 5', price: 6000, groupSize: 5, remaining: 8, note: null },
  ],
  genders: ['Female', 'Male', 'Non-binary', 'Prefer to self-describe', 'Prefer not to say'],
  hearAbout: ['Facebook', 'LinkedIn', 'Friend or colleague', 'School or organization', 'Sponsor', 'Other'],
  shirtSizes: ['XS', 'S', 'M', 'L', 'XL', '2XL'],
  consentText: 'I agree to the event terms and the processing of my data under the Data Privacy Act of 2012.',
  consentVersion: '2026-01',
  ...over,
})
