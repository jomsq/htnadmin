import { z } from 'zod'
import { fromManilaInput, isLocalDateTime } from './format'

// ---- API responses (phase3-contract.md, section 3A) ------------------------------

export const eventStatusSchema = z.enum(['Draft', 'OnSale', 'Closed', 'Ended'])
export type EventStatus = z.infer<typeof eventStatusSchema>

export const eventTabSchema = z.enum(['upcoming', 'drafts', 'past', 'all'])
export type EventTab = z.infer<typeof eventTabSchema>

export const proofModeSchema = z.enum(['Upload', 'ReplyEmail'])
export type ProofMode = z.infer<typeof proofModeSchema>

export const eventSummarySchema = z.object({
  id: z.string(),
  slug: z.string(),
  name: z.string(),
  status: eventStatusSchema,
  startsAt: z.string(),
  endsAt: z.string(),
  venue: z.string(),
  capacity: z.number(),
  sold: z.number(),
  coverUrl: z.string().nullable(),
  isMainEvent: z.boolean(),
})
export type EventSummary = z.infer<typeof eventSummarySchema>

/** The list is a JSON array; a paged `{ items }` wrapper is accepted too. */
export const eventListSchema = z.union([
  z.array(eventSummarySchema),
  z.object({ items: z.array(eventSummarySchema) }).transform((v) => v.items),
])

export const ticketVisibilitySchema = z.enum(['Public', 'Hidden'])
export type TicketVisibility = z.infer<typeof ticketVisibilitySchema>

export const ticketStatusSchema = z.enum(['OnSale', 'Scheduled', 'Ended', 'SoldOut', 'Hidden', 'Inactive'])
export type TicketStatus = z.infer<typeof ticketStatusSchema>

export const ticketTypeSchema = z.object({
  id: z.string(),
  name: z.string(),
  price: z.number(),
  quantity: z.number(),
  sold: z.number(),
  groupSize: z.number(),
  salesStartsAt: z.string().nullable(),
  salesEndsAt: z.string().nullable(),
  visibility: ticketVisibilitySchema,
  isActive: z.boolean(),
  sortOrder: z.number(),
  status: ticketStatusSchema,
})
export type TicketType = z.infer<typeof ticketTypeSchema>

export const voucherKindSchema = z.enum(['Percent', 'Fixed'])
export type VoucherKind = z.infer<typeof voucherKindSchema>

export const voucherSchema = z.object({
  id: z.string(),
  code: z.string(),
  kind: voucherKindSchema,
  value: z.number(),
  ticketTypeIds: z.array(z.string()),
  usageLimit: z.number().nullable(),
  used: z.number(),
  expiresAt: z.string().nullable(),
  isActive: z.boolean(),
  source: z.enum(['Manual', 'SpeakerPlusOne']),
})
export type Voucher = z.infer<typeof voucherSchema>

export const eventDetailSchema = eventSummarySchema.extend({
  description: z.string().nullable(),
  manualRegistration: z.boolean(),
  groupRegistration: z.boolean(),
  publicPage: z.boolean(),
  proofMode: proofModeSchema,
  membershipFee: z.number().nullable(),
  membershipYear: z.number().nullable(),
  tickets: z.array(ticketTypeSchema),
  vouchers: z.array(voucherSchema),
})
export type EventDetail = z.infer<typeof eventDetailSchema>

// ---- Shared field rules -------------------------------------------------------------

const DATE_HINT = 'Pick a date and time.'

/** Required datetime-local value (Asia/Manila). */
const localDateTime = z.string().refine(isLocalDateTime, DATE_HINT)
/** Optional datetime-local value: empty means "not set". */
const optionalLocalDateTime = z.string().refine((v) => v === '' || isLocalDateTime(v), DATE_HINT)

const amountRe = /^\d{1,7}(\.\d{1,2})?$/
const intRe = /^\d{1,7}$/

/** Pesos as typed ("1500", "1,500.50"); commas are allowed for grouping. */
export function parseAmount(v: string): number | null {
  const clean = v.trim().replace(/,/g, '')
  return amountRe.test(clean) ? Number(clean) : null
}

export function parseInteger(v: string): number | null {
  const clean = v.trim().replace(/,/g, '')
  return intRe.test(clean) ? Number(clean) : null
}

const slugRe = /^[a-z0-9]+(-[a-z0-9]+)*$/

// ---- Event info (create + Details › Event info) ---------------------------------------

export const eventInfoFormSchema = z
  .object({
    name: z.string().trim().min(1, 'Enter the event name.').max(120, 'Use 120 characters or fewer.'),
    slug: z
      .string()
      .trim()
      .toLowerCase()
      .refine((v) => v === '' || (v.length >= 3 && v.length <= 60 && slugRe.test(v)), 'Use 3 to 60 lower-case letters, numbers and single dashes.'),
    startsAt: localDateTime,
    endsAt: localDateTime,
    venue: z.string().trim().min(1, 'Enter the venue.').max(200, 'Use 200 characters or fewer.'),
    capacity: z.string(),
    description: z.string().trim().max(2000, 'Use 2,000 characters or fewer.'),
  })
  .superRefine((v, ctx) => {
    const capacity = parseInteger(v.capacity)
    if (capacity === null || capacity < 1 || capacity > 100000) {
      ctx.addIssue({ code: 'custom', path: ['capacity'], message: 'Enter a whole number from 1 to 100,000.' })
    }
    if (isLocalDateTime(v.startsAt) && isLocalDateTime(v.endsAt) && fromManilaInput(v.endsAt) <= fromManilaInput(v.startsAt)) {
      ctx.addIssue({ code: 'custom', path: ['endsAt'], message: 'The end must be after the start.' })
    }
  })
  .transform((v) => ({
    name: v.name,
    slug: v.slug === '' ? undefined : v.slug,
    startsAt: fromManilaInput(v.startsAt),
    endsAt: fromManilaInput(v.endsAt),
    venue: v.venue,
    capacity: parseInteger(v.capacity) ?? 0,
    description: v.description === '' ? undefined : v.description,
  }))
const eventInfoFieldNames = ['name', 'slug', 'startsAt', 'endsAt', 'venue', 'capacity', 'description'] as const
export type EventInfoFieldName = (typeof eventInfoFieldNames)[number]
/** Server field errors (camelCase) that belong to the event info form. */
export const isEventInfoField = (k: string): k is EventInfoFieldName => (eventInfoFieldNames as readonly string[]).includes(k)

export type EventInfoFormInput = z.input<typeof eventInfoFormSchema>
export type EventInfoBody = z.output<typeof eventInfoFormSchema>

// ---- Registration & payment -------------------------------------------------------------

export const registrationFormSchema = z
  .object({
    manualRegistration: z.boolean(),
    groupRegistration: z.boolean(),
    publicPage: z.boolean(),
    isMainEvent: z.boolean(),
    membershipFee: z.string(),
    membershipYear: z.string(),
    proofMode: proofModeSchema,
  })
  .superRefine((v, ctx) => {
    if (!v.isMainEvent) return
    const fee = parseAmount(v.membershipFee)
    if (fee === null || fee > 1_000_000) {
      ctx.addIssue({ code: 'custom', path: ['membershipFee'], message: 'Enter the fee in pesos, like 300.' })
    }
    const year = parseInteger(v.membershipYear)
    if (year === null || year < 2000 || year > 2100) {
      ctx.addIssue({ code: 'custom', path: ['membershipYear'], message: 'Enter a year like 2026.' })
    }
  })
  .transform((v) => ({
    manualRegistration: v.manualRegistration,
    groupRegistration: v.groupRegistration,
    publicPage: v.publicPage,
    proofMode: v.proofMode,
    isMainEvent: v.isMainEvent,
    membershipFee: v.isMainEvent ? parseAmount(v.membershipFee) : null,
    membershipYear: v.isMainEvent ? parseInteger(v.membershipYear) : null,
  }))
export type RegistrationFormInput = z.input<typeof registrationFormSchema>
export type RegistrationBody = z.output<typeof registrationFormSchema>

/** PUT /api/events/{id}: event info + registration settings. */
export type EventUpdateBody = Omit<EventInfoBody, 'slug' | 'description'> & { slug: string; description: string | null } & RegistrationBody

// ---- Ticket types ----------------------------------------------------------------------

export function ticketFormSchema(sold: number) {
  return z
    .object({
      name: z.string().trim().min(1, 'Enter a ticket name.').max(80, 'Use 80 characters or fewer.'),
      price: z.string(),
      quantity: z.string(),
      groupSize: z.string(),
      salesStartsAt: optionalLocalDateTime,
      salesEndsAt: optionalLocalDateTime,
      visibility: ticketVisibilitySchema,
    })
    .superRefine((v, ctx) => {
      const price = parseAmount(v.price)
      if (price === null) ctx.addIssue({ code: 'custom', path: ['price'], message: 'Enter a price like 1500 or 1500.50. Use 0 for free.' })
      else if (price > 1_000_000) ctx.addIssue({ code: 'custom', path: ['price'], message: 'Use ₱0 to ₱1,000,000.' })
      const quantity = parseInteger(v.quantity)
      if (quantity === null || quantity < 1 || quantity > 100000) {
        ctx.addIssue({ code: 'custom', path: ['quantity'], message: 'Enter a whole number from 1 to 100,000.' })
      } else if (quantity < sold) {
        ctx.addIssue({ code: 'custom', path: ['quantity'], message: `Can't be below the ${sold} already sold.` })
      }
      const group = parseInteger(v.groupSize)
      if (group === null || group < 1 || group > 50) {
        ctx.addIssue({ code: 'custom', path: ['groupSize'], message: 'Use 1 to 50 people.' })
      }
      if (v.salesStartsAt && v.salesEndsAt && isLocalDateTime(v.salesStartsAt) && isLocalDateTime(v.salesEndsAt)) {
        if (fromManilaInput(v.salesEndsAt) <= fromManilaInput(v.salesStartsAt)) {
          ctx.addIssue({ code: 'custom', path: ['salesEndsAt'], message: 'Sales must end after they start.' })
        }
      }
    })
    .transform((v) => ({
      name: v.name,
      price: parseAmount(v.price) ?? 0,
      quantity: parseInteger(v.quantity) ?? 0,
      groupSize: parseInteger(v.groupSize) ?? 1,
      salesStartsAt: v.salesStartsAt ? fromManilaInput(v.salesStartsAt) : null,
      salesEndsAt: v.salesEndsAt ? fromManilaInput(v.salesEndsAt) : null,
      visibility: v.visibility,
    }))
}
export type TicketFormInput = z.input<ReturnType<typeof ticketFormSchema>>
export type TicketBody = z.output<ReturnType<typeof ticketFormSchema>>

// ---- Vouchers ---------------------------------------------------------------------------

export const voucherFormSchema = z
  .object({
    code: z
      .string()
      .trim()
      .toUpperCase()
      .regex(/^[A-Z0-9-]{3,30}$/, 'Use 3 to 30 letters, numbers or dashes.'),
    kind: voucherKindSchema,
    value: z.string(),
    ticketTypeIds: z.array(z.string()),
    usageLimit: z.string(),
    expiresAt: optionalLocalDateTime,
  })
  .superRefine((v, ctx) => {
    const value = parseAmount(v.value)
    if (v.kind === 'Percent') {
      if (value === null || value < 1 || value > 100) ctx.addIssue({ code: 'custom', path: ['value'], message: 'Use 1 to 100 percent.' })
    } else if (value === null || value <= 0 || value > 1_000_000) {
      ctx.addIssue({ code: 'custom', path: ['value'], message: 'Enter an amount more than ₱0, like 300.' })
    }
    if (v.usageLimit.trim() !== '') {
      const limit = parseInteger(v.usageLimit)
      if (limit === null || limit < 1) ctx.addIssue({ code: 'custom', path: ['usageLimit'], message: 'Use a whole number of 1 or more, or leave it empty.' })
    }
  })
  .transform((v) => ({
    code: v.code,
    kind: v.kind,
    value: parseAmount(v.value) ?? 0,
    ticketTypeIds: v.ticketTypeIds,
    usageLimit: v.usageLimit.trim() === '' ? null : parseInteger(v.usageLimit),
    expiresAt: v.expiresAt ? fromManilaInput(v.expiresAt) : null,
  }))
export type VoucherFormInput = z.input<typeof voucherFormSchema>
export type VoucherBody = z.output<typeof voucherFormSchema>

/** The current event as a PUT body, so one section can save without touching the others. */
export function updateBodyFrom(e: EventDetail): EventUpdateBody {
  return {
    name: e.name,
    slug: e.slug,
    startsAt: e.startsAt,
    endsAt: e.endsAt,
    venue: e.venue,
    capacity: e.capacity,
    description: e.description ?? null,
    manualRegistration: e.manualRegistration,
    groupRegistration: e.groupRegistration,
    publicPage: e.publicPage,
    proofMode: e.proofMode,
    isMainEvent: e.isMainEvent,
    membershipFee: e.membershipFee,
    membershipYear: e.membershipYear,
  }
}
