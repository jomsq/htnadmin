/** API fixtures shaped like phase3-contract.md section 3A. */

export const summary = (over: Record<string, unknown> = {}) => ({
  id: 'e1',
  slug: 'htn-con-2026',
  name: 'HTN Con 2026',
  status: 'OnSale',
  startsAt: '2026-11-22T00:00:00Z',
  endsAt: '2026-11-22T10:00:00Z',
  venue: 'SMX Convention Center, Pasay',
  capacity: 400,
  sold: 312,
  coverUrl: null,
  isMainEvent: true,
  ...over,
})

export const ticket = (over: Record<string, unknown> = {}) => ({
  id: 't1',
  name: 'Regular',
  price: 1500,
  quantity: 200,
  sold: 121,
  groupSize: 1,
  salesStartsAt: '2026-10-16T00:00:00+08:00',
  salesEndsAt: '2026-11-20T23:59:00+08:00',
  visibility: 'Public',
  isActive: true,
  sortOrder: 1,
  status: 'OnSale',
  ...over,
})

export const voucher = (over: Record<string, unknown> = {}) => ({
  id: 'v1',
  code: 'STUDENT20',
  kind: 'Percent',
  value: 20,
  ticketTypeIds: ['t2'],
  usageLimit: 50,
  used: 12,
  expiresAt: '2026-11-15T23:59:00+08:00',
  isActive: true,
  source: 'Manual',
  ...over,
})

export const detail = (over: Record<string, unknown> = {}) => ({
  ...summary(),
  description: 'The yearly HackTheNorth.PH conference.',
  manualRegistration: true,
  groupRegistration: true,
  publicPage: true,
  proofMode: 'Upload',
  membershipFee: 300,
  membershipYear: 2026,
  tickets: [
    ticket(),
    ticket({ id: 't2', name: 'Student', price: 800, quantity: 40, sold: 31, sortOrder: 2 }),
    ticket({ id: 't3', name: 'Team of 5', price: 6000, quantity: 10, sold: 2, groupSize: 5, sortOrder: 3 }),
    ticket({ id: 't4', name: 'Sponsor comp', price: 0, quantity: 30, sold: 8, visibility: 'Hidden', salesStartsAt: null, salesEndsAt: null, status: 'Hidden', sortOrder: 4 }),
  ],
  vouchers: [voucher(), voucher({ id: 'v2', code: 'PARTNER300', kind: 'Fixed', value: 300, ticketTypeIds: [], usageLimit: null, used: 4 })],
  ...over,
})

export const draftDetail = (over: Record<string, unknown> = {}) =>
  detail({ id: 'e3', slug: 'student-ctf', name: 'Student CTF', status: 'Draft', sold: 0, tickets: [], vouchers: [], isMainEvent: false, membershipFee: null, membershipYear: null, ...over })
