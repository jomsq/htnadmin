import { z } from 'zod'

export const levelSchema = z.enum(['Board', 'Goon', 'Apprentice', 'Member'])
export type Level = z.infer<typeof levelSchema>

export const accountStatusSchema = z.enum(['Active', 'InvitePending', 'NoLogin', 'Deactivated'])
export type AccountStatus = z.infer<typeof accountStatusSchema>

export const personSchema = z.object({
  id: z.string(),
  firstName: z.string(),
  middleName: z.string().nullish(),
  lastName: z.string(),
  email: z.string(),
  level: levelSchema,
  position: z.string().nullish(),
  isActive: z.boolean(),
  // null for non-board viewers (the API hides account details from them)
  accountStatus: accountStatusSchema.nullable(),
  lastSignInAt: z.string().nullish(),
  createdAt: z.string(),
  isCfo: z.boolean().default(false),
  isFinanceAssistant: z.boolean().default(false),
})
export type Person = z.infer<typeof personSchema>

export const peoplePageSchema = z.object({
  items: z.array(personSchema),
  total: z.number(),
  page: z.number(),
  pageSize: z.number(),
})
export type PeoplePage = z.infer<typeof peoplePageSchema>

const optionalText = (max: number) =>
  z
    .string()
    .trim()
    .max(max, `Use ${max} characters or fewer.`)
    .transform((v) => (v === '' ? undefined : v))
    .optional()

export const createPersonSchema = z.object({
  firstName: z.string().trim().min(1, 'Enter a first name.').max(100, 'Use 100 characters or fewer.'),
  middleName: optionalText(100),
  lastName: z.string().trim().min(1, 'Enter a last name.').max(100, 'Use 100 characters or fewer.'),
  email: z.string().trim().pipe(z.email('Enter a valid email address.')),
  position: optionalText(120),
  level: levelSchema,
})
export type CreatePersonInput = z.input<typeof createPersonSchema>
export type CreatePersonBody = z.output<typeof createPersonSchema>

export const levelLabels: Record<Level, string> = {
  Board: 'Board',
  Goon: 'Goon',
  Apprentice: 'Apprentice',
  Member: 'Member',
}

/** Access note shown under the level picker (People.dc.html). */
export const accessNotes: Record<Level, string> = {
  Board:
    'Gets a login invite by email. They set their own password (admins never see it) and must turn on 2-step verification. Finance is visible to board accounts.',
  Goon: 'Gets a login invite by email with limited access: their events, committee board, tasks and leaderboard. The invite link expires in 48 hours.',
  Apprentice:
    'Gets a login invite by email with limited access: their committee board, tasks and leaderboard. The invite link expires in 48 hours.',
  Member:
    'No login, roster only. A member must have attended at least one event first: search their attendee record below. Membership is paid once per main-event year, usually at the event (POS); record it here if paid elsewhere.',
}

// ---- Person detail, history, promotion -----------------------------------------

export const positionKindSchema = z.enum(['BoardSeat', 'GoonPosition'])
export type PositionKind = z.infer<typeof positionKindSchema>

export const historyKindSchema = z.enum(['Created', 'LevelChanged', 'PositionChanged', 'Deactivated', 'PositionAssigned', 'PositionVacated'])
export type HistoryKind = z.infer<typeof historyKindSchema>

export const historyEntrySchema = z.object({
  at: z.string(),
  kind: historyKindSchema.or(z.string()),
  from: z.string().nullable(),
  to: z.string().nullable(),
  reason: z.string().nullable(),
  actorName: z.string().nullable(),
})
export type HistoryEntry = z.infer<typeof historyEntrySchema>

export const personDetailSchema = z.object({
  person: personSchema,
  positions: z.array(z.object({ id: z.string(), title: z.string(), kind: positionKindSchema })),
  history: z.array(historyEntrySchema),
})
export type PersonDetail = z.infer<typeof personDetailSchema>

/** Rank used to tell promotions from demotions. Board is a separate seat above the path. */
export const levelRank: Record<Level, number> = { Member: 0, Apprentice: 1, Goon: 2, Board: 3 }

/** The path Member → Apprentice → Goon. */
export const pathLevels = ['Member', 'Apprentice', 'Goon'] as const satisfies ReadonlyArray<Level>

/** Next step on the path, or null (Goon and Board have no next step; Board is a seat). */
export function nextLevel(level: Level): Level | null {
  if (level === 'Member') return 'Apprentice'
  if (level === 'Apprentice') return 'Goon'
  return null
}

export function isDemotion(from: Level, to: Level): boolean {
  return levelRank[to] < levelRank[from]
}

/** Levels a person may move to: the next step, Board, or any lower level (demotion, reason required). */
export function allowedTargets(from: Level): Level[] {
  const next = nextLevel(from)
  return levelSchema.options
    .filter((l) => l !== from && (l === next || l === 'Board' || isDemotion(from, l)))
    .sort((a, b) => levelRank[b] - levelRank[a])
}

export const promoteFormSchema = z.object({
  toLevel: levelSchema,
  reason: z.string().trim().max(500, 'Use 500 characters or fewer.'),
})
export type PromoteFormValues = z.infer<typeof promoteFormSchema>
export type PromoteBody = { toLevel: Level; reason: string | null }

export const editPersonSchema = z.object({
  firstName: z.string().trim().min(1, 'Enter a first name.').max(100, 'Use 100 characters or fewer.'),
  middleName: optionalText(100),
  lastName: z.string().trim().min(1, 'Enter a last name.').max(100, 'Use 100 characters or fewer.'),
  email: z.string().trim().pipe(z.email('Enter a valid email address.')),
  position: optionalText(120),
})
export type EditPersonInput = z.input<typeof editPersonSchema>
export type EditPersonValues = z.output<typeof editPersonSchema>
export type EditPersonBody = { firstName: string; middleName: string | null; lastName: string; email: string; position: string | null }

export const deactivateSchema = z.object({
  reason: z.string().trim().min(1, 'Enter a reason. It is kept in their history.').max(500, 'Use 500 characters or fewer.'),
})
export type DeactivateValues = z.infer<typeof deactivateSchema>

// ---- CSV import -------------------------------------------------------------------

export const importRowSchema = z.object({
  row: z.number(),
  firstName: z.string().nullish(),
  lastName: z.string().nullish(),
  email: z.string().nullish(),
  level: z.string().nullish(),
  errors: z.array(z.string()),
})
export type ImportRow = z.infer<typeof importRowSchema>

export const importResultSchema = z.object({
  rows: z.array(importRowSchema),
  valid: z.number(),
  invalid: z.number(),
  created: z.number(),
})
export type ImportResult = z.infer<typeof importResultSchema>

export const IMPORT_MAX_BYTES = 1024 * 1024
export const IMPORT_HEADER = 'first_name,middle_name,last_name,email,level,position'

// ---- Positions and finance roles -------------------------------------------------

export const personRefSchema = z.object({ id: z.string(), firstName: z.string(), lastName: z.string(), level: levelSchema })
export type PersonRef = z.infer<typeof personRefSchema>

export const positionSchema = z.object({
  id: z.string(),
  kind: positionKindSchema,
  title: z.string(),
  sortOrder: z.number(),
  isActive: z.boolean(),
  holder: personRefSchema.nullable(),
})
export type Position = z.infer<typeof positionSchema>

export const positionFormSchema = z.object({
  kind: positionKindSchema,
  title: z.string().trim().min(1, 'Enter a title.').max(120, 'Use 120 characters or fewer.'),
})
export type PositionFormValues = z.infer<typeof positionFormSchema>

export const kindLabels: Record<PositionKind, string> = { BoardSeat: 'Board seat', GoonPosition: 'Goon position' }

/** Level a holder of this kind of position must have. */
export const levelForKind: Record<PositionKind, Level> = { BoardSeat: 'Board', GoonPosition: 'Goon' }

export const financeRolesSchema = z.object({ cfo: personRefSchema.nullable(), assistant: personRefSchema.nullable() })
export type FinanceRoles = z.infer<typeof financeRolesSchema>
export type FinanceRolesBody = { cfoPersonId: string | null; assistantPersonId: string | null }
