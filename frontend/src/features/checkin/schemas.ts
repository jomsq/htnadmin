import { z } from 'zod'

/** Shapes from phase3-contract.md section 3C. */

/** Lookup = the payload was a participant ID (typed or QR'd), not a signed ticket: staff must confirm. */
export const scanResultKindSchema = z.enum(['Welcome', 'AlreadyCheckedIn', 'NotPaid', 'Cancelled', 'Invalid', 'Lookup'])
export type ScanResultKind = z.infer<typeof scanResultKindSchema>

export const scanAttendeeSchema = z.object({
  name: z.string(),
  ticketName: z.string(),
  participantId: z.string(),
  shirtSize: z.string().nullish(),
  isMember: z.boolean().default(false),
})
export type ScanAttendee = z.infer<typeof scanAttendeeSchema>

export const attendeeStatusSchema = z.enum(['PendingPayment', 'Paid', 'CheckedIn', 'Cancelled'])
export type AttendeeStatus = z.infer<typeof attendeeStatusSchema>

export const scanResponseSchema = z.object({
  result: scanResultKindSchema,
  /** Lookup only: who to check in with POST /manual, and where their registration stands. */
  attendeeId: z.string().nullish(),
  status: attendeeStatusSchema.nullish(),
  attendee: scanAttendeeSchema.nullable(),
  checkedInAt: z.string().nullish(),
  checkedInStation: z.string().nullish(),
  amountDue: z.number().nullish(),
})
export type ScanResponse = z.infer<typeof scanResponseSchema>

export const checkInStatsSchema = z.object({
  checkedIn: z.number(),
  paid: z.number(),
  thisStation: z.number(),
  stationsOnline: z.number(),
})
export type CheckInStats = z.infer<typeof checkInStatsSchema>

export const recentScanSchema = z.object({
  name: z.string().nullish(),
  participantId: z.string().nullish(),
  result: scanResultKindSchema,
  at: z.string(),
})
export type RecentScan = z.infer<typeof recentScanSchema>
export const recentScansSchema = z.array(recentScanSchema)

export const searchStatusSchema = attendeeStatusSchema

export const searchResultSchema = z.object({
  attendeeId: z.string(),
  name: z.string(),
  participantId: z.string(),
  ticketName: z.string(),
  status: searchStatusSchema,
})
export type SearchResult = z.infer<typeof searchResultSchema>
export const searchResultsSchema = z.array(searchResultSchema)

export type ScanBody = { payload: string; station: string }
export type ManualBody = { attendeeId: string; station: string }

/** Station names are free text; keep them short so they fit the result card and stats. */
export const STATION_MAX = 60
export const DEFAULT_STATION = 'Main entrance'
