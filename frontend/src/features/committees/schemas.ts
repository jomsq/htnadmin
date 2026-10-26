import { z } from 'zod'
import { levelSchema, type Level } from '@/features/organization/schemas'

// ---- API responses (phase4-contract.md, section 4A) ---------------------------------

export const committeePersonSchema = z.object({
  personId: z.string(),
  firstName: z.string(),
  lastName: z.string(),
  level: levelSchema,
  joinedAt: z.string().nullish(),
})
export type CommitteePerson = z.infer<typeof committeePersonSchema>

export const committeeVolunteerSchema = z.object({
  id: z.string(),
  name: z.string(),
  email: z.string().nullish(),
  mobile: z.string().nullish(),
})
export type CommitteeVolunteer = z.infer<typeof committeeVolunteerSchema>

export const committeeSchema = z.object({
  id: z.string(),
  name: z.string(),
  sortOrder: z.number().default(0),
  isActive: z.boolean().default(true),
  /** Board, or the lead of this committee. The server enforces it too. */
  canManage: z.boolean().default(false),
  openTasks: z.number().default(0),
  peopleCount: z.number().default(0),
  lead: committeePersonSchema.nullable().default(null),
  members: z.array(committeePersonSchema).default([]),
  volunteers: z.array(committeeVolunteerSchema).default([]),
})
export type Committee = z.infer<typeof committeeSchema>

export const committeesPageSchema = z.object({
  canCreateCommittee: z.boolean().default(false),
  canManageVolunteers: z.boolean().default(false),
  committees: z.array(committeeSchema).default([]),
  pool: z
    .object({
      people: z.array(committeePersonSchema).default([]),
      volunteers: z.array(committeeVolunteerSchema).default([]),
    })
    .default({ people: [], volunteers: [] }),
})
export type CommitteesPage = z.infer<typeof committeesPageSchema>

// ---- Forms ---------------------------------------------------------------------------

export const committeeFormSchema = z.object({
  name: z.string().trim().min(1, 'Enter a committee name.').max(60, 'Use 60 characters or fewer.'),
})
export type CommitteeFormInput = z.input<typeof committeeFormSchema>
export type CommitteeBody = z.output<typeof committeeFormSchema>

export const reasonFormSchema = z.object({
  reason: z.string().trim().min(1, 'Enter a reason.').max(500, 'Use 500 characters or fewer.'),
})
export type ReasonFormInput = z.input<typeof reasonFormSchema>
export type ReasonBody = z.output<typeof reasonFormSchema>

export const leadFormSchema = z.object({
  personId: z.string().min(1, 'Pick a goon.'),
})
export type LeadFormInput = z.input<typeof leadFormSchema>

export const assignFormSchema = z.object({
  committeeId: z.string().min(1, 'Pick a committee.'),
})
export type AssignFormInput = z.input<typeof assignFormSchema>

const optionalText = (max: number, message: string) =>
  z
    .string()
    .trim()
    .max(max, message)
    .transform((v) => (v === '' ? undefined : v))

export const volunteerFormSchema = z.object({
  name: z.string().trim().min(1, 'Enter the volunteer’s name.').max(120, 'Use 120 characters or fewer.'),
  email: z
    .string()
    .trim()
    .refine((v) => v === '' || z.string().pipe(z.email()).safeParse(v).success, 'Enter a valid email address.')
    .transform((v) => (v === '' ? undefined : v)),
  mobile: optionalText(30, 'Use 30 characters or fewer.'),
  committeeId: optionalText(100, 'Use 100 characters or fewer.'),
})
export type VolunteerFormInput = z.input<typeof volunteerFormSchema>
export type VolunteerBody = z.output<typeof volunteerFormSchema>

// ---- Display helpers -------------------------------------------------------------------

export function personName(p: Pick<CommitteePerson, 'firstName' | 'lastName'>): string {
  return `${p.firstName} ${p.lastName}`.trim()
}

/** "Mika Soriano" → "MS". Falls back to the first two letters of a single name. */
export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '?'
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase()
}

export function countLabel(n: number, one: string, many: string): string {
  return `${n} ${n === 1 ? one : many}`
}

/** Levels that can be put on a committee (Members are roster only, volunteers are separate). */
export const COMMITTEE_LEVELS: readonly Level[] = ['Board', 'Goon', 'Apprentice']
