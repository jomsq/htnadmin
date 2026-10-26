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
  accountStatus: accountStatusSchema,
  lastSignInAt: z.string().nullish(),
  createdAt: z.string(),
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
