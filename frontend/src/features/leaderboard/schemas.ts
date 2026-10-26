import { z } from 'zod'

// ---- Leaderboard (phase4-contract.md, section 4C) ---------------------------

export const LEADERBOARD_SCOPES = ['event', 'quarter', 'alltime'] as const
export const leaderboardScopeSchema = z.enum(LEADERBOARD_SCOPES)
export type LeaderboardScope = (typeof LEADERBOARD_SCOPES)[number]

/** Segment labels from the canvas (frame 14). The event segment shows the event's name instead. */
export const SCOPE_LABELS: Record<LeaderboardScope, string> = {
  event: 'By event',
  quarter: 'This quarter',
  alltime: 'All time',
}

/** The board is goons and apprentices only, so those are the only levels a row can carry. */
export const leaderboardLevelSchema = z.enum(['Goon', 'Apprentice'])
export type LeaderboardLevel = z.infer<typeof leaderboardLevelSchema>

/** Goon = brand, Apprentice = teal (canvas frame 14). */
export const LEVEL_BADGE: Record<LeaderboardLevel, 'brand' | 'teal'> = { Goon: 'brand', Apprentice: 'teal' }

export const leaderboardRowSchema = z.object({
  rank: z.number(),
  personId: z.string(),
  name: z.string(),
  initials: z.string(),
  level: leaderboardLevelSchema,
  /** Committee names for the scope, already joined by the server ("Logistics", "Logistics, Programs"). */
  committees: z.string().default(''),
  tasksDone: z.number().default(0),
  asLead: z.number().default(0),
  asCollab: z.number().default(0),
  asAssistant: z.number().default(0),
  points: z.number().default(0),
  isMe: z.boolean().default(false),
})
export type LeaderboardRow = z.infer<typeof leaderboardRowSchema>

const namedSchema = z.object({ id: z.string(), name: z.string() })
export type NamedOption = z.infer<typeof namedSchema>

export const leaderboardSchema = z.object({
  scope: leaderboardScopeSchema,
  scopeLabel: z.string(),
  eventId: z.string().nullish(),
  committeeId: z.string().nullish(),
  committees: z.array(namedSchema).default([]),
  events: z.array(namedSchema).default([]),
  rows: z.array(leaderboardRowSchema).default([]),
  /** The caller's own row, or null when they are not on the board (board members are excluded). */
  me: leaderboardRowSchema.nullish(),
})
export type Leaderboard = z.infer<typeof leaderboardSchema>

const pointsFmt = new Intl.NumberFormat('en-PH', { maximumFractionDigits: 2 })

/** 412 → "412", 28.7 → "28.7" (awards use a 0.7/0.3 multiplier, so points can be fractional). */
export function formatPoints(n: number): string {
  return pointsFmt.format(n)
}

/** "9 / 6 / 3" for the lead / collab / assistant split. */
export function formatSplit(row: LeaderboardRow): string {
  return `${row.asLead} / ${row.asCollab} / ${row.asAssistant}`
}
