import { z } from 'zod'
import { contrastWithWhite, isHexColor, MIN_CONTRAST } from '@/app/theme/brand'
import { proofModeSchema } from '@/features/events/schemas'
import { fromManilaInput, isLocalDateTime } from '@/features/events/format'

// ---- Shared enums (phase3-contract.md, section 3B) -----------------------------------------

export const registrationStatusSchema = z.enum(['PendingPayment', 'Paid', 'CheckedIn', 'Cancelled'])
export type RegistrationStatus = z.infer<typeof registrationStatusSchema>

export const paymentMethodSchema = z.enum(['GCash', 'Bank', 'Cash', 'Comp'])
export type PaymentMethod = z.infer<typeof paymentMethodSchema>

export const registrationSourceSchema = z.enum(['Public', 'Manual'])

// ---- Admin responses ------------------------------------------------------------------------

export const registrationRowSchema = z.object({
  id: z.string(),
  buyerName: z.string(),
  buyerEmail: z.string(),
  attendeeCount: z.number(),
  participantIds: z.array(z.string()),
  ticketName: z.string(),
  total: z.number(),
  status: registrationStatusSchema,
  overdue: z.boolean(),
  method: paymentMethodSchema.nullable(),
  reference: z.string().nullable(),
  hasProof: z.boolean(),
  createdAt: z.string(),
  source: registrationSourceSchema,
})
export type RegistrationRow = z.infer<typeof registrationRowSchema>

export const registrationCountsSchema = z.object({
  all: z.number(),
  pendingPayment: z.number(),
  paid: z.number(),
  checkedIn: z.number(),
  cancelled: z.number(),
  overdue: z.number(),
})
export type RegistrationCounts = z.infer<typeof registrationCountsSchema>

export const registrationListSchema = z.object({
  items: z.array(registrationRowSchema),
  total: z.number(),
  page: z.number(),
  pageSize: z.number(),
  counts: registrationCountsSchema,
})
export type RegistrationList = z.infer<typeof registrationListSchema>

export const attendeeSchema = z.object({
  id: z.string(),
  participantId: z.string(),
  firstName: z.string(),
  middleName: z.string().nullish(),
  lastName: z.string(),
  email: z.string().nullish(),
  mobile: z.string().nullish(),
  gender: z.string().nullish(),
  occupation: z.string().nullish(),
  company: z.string().nullish(),
  shirtSize: z.string().nullish(),
  hearAbout: z.string().nullish(),
  checkedInAt: z.string().nullish(),
})
export type Attendee = z.infer<typeof attendeeSchema>

export const paymentSchema = z.object({
  method: paymentMethodSchema,
  paymentAccountId: z.string().nullish(),
  paymentAccountLabel: z.string().nullish(),
  reference: z.string().nullish(),
  amount: z.number(),
  receivedAt: z.string().nullish(),
  markedByName: z.string().nullish(),
})
export type Payment = z.infer<typeof paymentSchema>

export const proofSchema = z.object({
  reference: z.string().nullish(),
  uploadedAt: z.string(),
  url: z.string(),
})
export type Proof = z.infer<typeof proofSchema>

export const historyEntrySchema = z.object({
  at: z.string(),
  kind: z.string(),
  actorName: z.string().nullish(),
  reason: z.string().nullish(),
})
export type RegistrationHistoryEntry = z.infer<typeof historyEntrySchema>

export const registrationDetailSchema = registrationRowSchema.extend({
  attendees: z.array(attendeeSchema),
  subtotal: z.number(),
  discount: z.number(),
  voucherCode: z.string().nullish(),
  deadline: z.string().nullish(),
  payment: paymentSchema.nullable(),
  proof: proofSchema.nullable(),
  history: z.array(historyEntrySchema),
})
export type RegistrationDetail = z.infer<typeof registrationDetailSchema>

export const markPaidResultSchema = z.object({ updated: z.number() })

export const emailSettingsSchema = z.object({
  accentColor: z.string().nullable(),
  paymentAccountIds: z.array(z.string()),
  paymentDeadlineHours: z.number().nullable(),
})
export type EmailSettings = z.infer<typeof emailSettingsSchema>

export type EmailTemplate = 'billing' | 'ticket'

// ---- Public responses -----------------------------------------------------------------------

export const publicTicketSchema = z.object({
  id: z.string(),
  name: z.string(),
  price: z.number(),
  groupSize: z.number(),
  remaining: z.number(),
  note: z.string().nullish(),
})
export type PublicTicket = z.infer<typeof publicTicketSchema>

export const publicEventSchema = z.object({
  name: z.string(),
  startsAt: z.string(),
  endsAt: z.string(),
  venue: z.string(),
  description: z.string().nullish(),
  coverUrl: z.string().nullish(),
  logoUrl: z.string().nullish(),
  brandColor: z.string().nullish(),
  groupRegistration: z.boolean(),
  proofMode: proofModeSchema,
  tickets: z.array(publicTicketSchema),
  genders: z.array(z.string()),
  hearAbout: z.array(z.string()),
  shirtSizes: z.array(z.string()),
  consentText: z.string(),
  consentVersion: z.union([z.string(), z.number()]),
})
export type PublicEvent = z.infer<typeof publicEventSchema>

export const voucherCheckSchema = z.object({ valid: z.boolean(), discount: z.number(), total: z.number() })
export type VoucherCheck = z.infer<typeof voucherCheckSchema>

export const registrationResultSchema = z.object({
  participantIds: z.array(z.string()),
  amountDue: z.number(),
  deadline: z.string().nullish(),
  proofMode: proofModeSchema,
  email: z.string(),
  /** Not in the contract yet: if the API returns the proof-upload token, the success screen links to it. */
  proofToken: z.string().nullish(),
})
export type RegistrationResult = z.infer<typeof registrationResultSchema>

export const proofInfoSchema = z.object({
  eventName: z.string(),
  participantId: z.string(),
  amountDue: z.number(),
  deadline: z.string().nullish(),
  uploaded: z.boolean(),
})
export type ProofInfo = z.infer<typeof proofInfoSchema>

// ---- Field rules ----------------------------------------------------------------------------

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

const requiredName = (what: string) => z.string().trim().min(1, `Enter ${what}.`).max(60, 'Use 60 characters or fewer.')
const optionalText = (max: number) => z.string().trim().max(max, `Use ${max} characters or fewer.`)
const email = z
  .string()
  .trim()
  .min(1, 'Enter an email address.')
  .max(254, 'Use 254 characters or fewer.')
  .refine((v) => EMAIL_RE.test(v), 'Enter an email address like name@example.com.')

/**
 * Philippine mobile number as typed after the +63 prefix ("917 123 4567"), or with +63 / 63 / 0 in front.
 * Returns "+639171234567", or null when it isn't a valid mobile number.
 */
export function normalizeMobile(value: string): string | null {
  let digits = value.replace(/[\s().-]/g, '')
  if (digits.startsWith('+63')) digits = digits.slice(3)
  else if (digits.startsWith('63') && digits.length === 12) digits = digits.slice(2)
  else if (digits.startsWith('0') && digits.length === 11) digits = digits.slice(1)
  return /^9\d{9}$/.test(digits) ? `+63${digits}` : null
}

export const MOBILE_MESSAGE = 'Enter a mobile number like 917 123 4567.'

export type FormOptions = { genders: string[]; hearAbout: string[]; shirtSizes: string[] }

/** Options shown when the API doesn't send any (manual registration on a closed event). Matches canvas frame 10. */
export const defaultFormOptions: FormOptions = {
  genders: ['Female', 'Male', 'Non-binary', 'Prefer to self-describe', 'Prefer not to say'],
  hearAbout: ['Facebook', 'LinkedIn', 'Friend or colleague', 'School or organization', 'Sponsor', 'Other'],
  shirtSizes: ['XS', 'S', 'M', 'L', 'XL', '2XL'],
}

const oneOfOrEmpty = (options: string[], message: string) => z.string().refine((v) => v === '' || options.includes(v), message)

export type BuyerValues = {
  firstName: string
  middleName: string
  lastName: string
  gender: string
  email: string
  mobile: string
  occupation: string
  company: string
  shirtSize: string
  hearAbout: string
}

export type MemberValues = { firstName: string; lastName: string; email: string }

export type MarkPaidValues = { method: PaymentMethod; paymentAccountId: string; reference: string; receivedAt: string }

/** Form values for public and manual registration (one shape so the field components are shared). */
export type RegistrationFormValues = {
  ticketTypeId: string
  buyer: BuyerValues
  members: MemberValues[]
  voucherCode: string
  consent: boolean
  /** Honeypot: must stay empty. Public form only. */
  website: string
  /** Manual registration only: record the payment right away. */
  markPaid: boolean
  payment: MarkPaidValues
}

export const emptyBuyer: BuyerValues = {
  firstName: '',
  middleName: '',
  lastName: '',
  gender: '',
  email: '',
  mobile: '',
  occupation: '',
  company: '',
  shirtSize: '',
  hearAbout: '',
}

export const emptyMember: MemberValues = { firstName: '', lastName: '', email: '' }

function buyerSchema(o: FormOptions) {
  return z.object({
    firstName: requiredName('the first name'),
    middleName: optionalText(60),
    lastName: requiredName('the last name'),
    gender: o.genders.length
      ? z.string().refine((v) => o.genders.includes(v), 'Select a gender.')
      : z.string(),
    email,
    mobile: z.string().refine((v) => normalizeMobile(v) !== null, MOBILE_MESSAGE),
    occupation: optionalText(120),
    company: optionalText(120),
    shirtSize: oneOfOrEmpty(o.shirtSizes, 'Pick a shirt size.'),
    hearAbout: oneOfOrEmpty(o.hearAbout, 'Pick an option.'),
  })
}

const memberSchema = z.object({
  firstName: requiredName('the first name'),
  lastName: requiredName('the last name'),
  email,
})

const markPaidShape = (needsAccount: (m: PaymentMethod) => boolean) =>
  z
    .object({
      method: paymentMethodSchema,
      paymentAccountId: z.string(),
      reference: z.string().trim().max(60, 'Use 60 characters or fewer.'),
      receivedAt: z.string().refine((v) => v === '' || isLocalDateTime(v), 'Pick a date and time.'),
    })
    .superRefine((v, ctx) => {
      if (needsAccount(v.method) && !v.paymentAccountId) {
        ctx.addIssue({ code: 'custom', path: ['paymentAccountId'], message: 'Pick the account that received the payment.' })
      }
    })

export function markPaidFormSchema(needsAccount: (m: PaymentMethod) => boolean) {
  return markPaidShape(needsAccount)
}

export function registrationFormSchema(o: FormOptions & { mode: 'public' | 'manual'; needsAccount?: (m: PaymentMethod) => boolean }) {
  return z
    .object({
      ticketTypeId: z.string().min(1, 'Pick a ticket.'),
      buyer: buyerSchema(o),
      members: z.array(memberSchema),
      voucherCode: z
        .string()
        .trim()
        .refine((v) => v === '' || /^[A-Za-z0-9-]{3,30}$/.test(v), "This voucher code can't be used."),
      consent: z.boolean().refine((v) => v, o.mode === 'public' ? 'Please agree to continue.' : 'Confirm the attendee agreed to the data privacy terms.'),
      website: z.string(),
      markPaid: z.boolean(),
      payment: z.object({ method: paymentMethodSchema, paymentAccountId: z.string(), reference: z.string(), receivedAt: z.string() }),
    })
    .superRefine((v, ctx) => {
      if (o.mode !== 'manual' || !v.markPaid) return
      const parsed = markPaidShape(o.needsAccount ?? (() => false)).safeParse(v.payment)
      if (parsed.success) return
      for (const issue of parsed.error.issues) ctx.addIssue({ code: 'custom', path: ['payment', ...issue.path.map(String)], message: issue.message })
    })
}

// ---- Request bodies -------------------------------------------------------------------------

export type AttendeeInput = {
  firstName: string
  middleName?: string
  lastName: string
  gender?: string
  email: string
  mobile?: string
  occupation?: string
  company?: string
  shirtSize?: string
  hearAbout?: string
}

export type MarkPaidBody = { method: PaymentMethod; paymentAccountId?: string; reference?: string; receivedAt?: string }

export type PublicRegistrationBody = {
  idempotencyKey: string
  ticketTypeId: string
  voucherCode?: string
  attendees: AttendeeInput[]
  consent: true
  website: string
}

export type ManualRegistrationBody = {
  ticketTypeId: string
  voucherCode?: string
  attendees: AttendeeInput[]
  consent: true
  markPaid?: MarkPaidBody
}

const opt = (v: string): string | undefined => (v.trim() === '' ? undefined : v.trim())

export function toAttendees(v: Pick<RegistrationFormValues, 'buyer' | 'members'>): AttendeeInput[] {
  const b = v.buyer
  const buyer: AttendeeInput = {
    firstName: b.firstName.trim(),
    middleName: opt(b.middleName),
    lastName: b.lastName.trim(),
    gender: opt(b.gender),
    email: b.email.trim(),
    mobile: normalizeMobile(b.mobile) ?? undefined,
    occupation: opt(b.occupation),
    company: opt(b.company),
    shirtSize: opt(b.shirtSize),
    hearAbout: opt(b.hearAbout),
  }
  return [buyer, ...v.members.map((m) => ({ firstName: m.firstName.trim(), lastName: m.lastName.trim(), email: m.email.trim() }))]
}

export function toMarkPaidBody(p: MarkPaidValues): MarkPaidBody {
  const noAccount = p.method === 'Comp'
  return {
    method: p.method,
    paymentAccountId: noAccount ? undefined : opt(p.paymentAccountId),
    reference: noAccount ? undefined : opt(p.reference),
    receivedAt: p.receivedAt ? fromManilaInput(p.receivedAt) : undefined,
  }
}

export function voucherOrUndefined(code: string): string | undefined {
  const c = code.trim().toUpperCase()
  return c === '' ? undefined : c
}

/**
 * Server field errors use the request shape ("attendees[1].email", PascalCase or camelCase).
 * Maps them onto the form paths ("members.0.email"). Unknown keys return null.
 */
export function formPathForServerField(key: string): string | null {
  const m = /^attendees\[(\d+)\]\.(\w+)$/i.exec(key)
  if (m) {
    const index = Number(m[1])
    const field = m[2].charAt(0).toLowerCase() + m[2].slice(1)
    return index === 0 ? `buyer.${field}` : `members.${index - 1}.${field}`
  }
  if (key === 'voucherCode' || key === 'ticketTypeId' || key === 'consent') return key
  return null
}

// ---- Reason dialogs (cancel, revert payment) --------------------------------------------------

export const reasonSchema = z.object({
  reason: z.string().trim().min(1, 'Enter a reason.').max(500, 'Use 500 characters or fewer.'),
})
export type ReasonValues = z.infer<typeof reasonSchema>

// ---- Email settings form ----------------------------------------------------------------------

export const deadlineChoices = ['24', '48', '72'] as const
export type DeadlineChoice = (typeof deadlineChoices)[number] | 'custom'

export type EmailSettingsFormValues = {
  accentColor: string
  paymentAccountIds: string[]
  deadline: DeadlineChoice
  customHours: string
}

/** `hasAccounts`: when Settings has payment accounts, the billing email must list at least one. */
export function emailSettingsFormSchema(hasAccounts: boolean) {
  return z
    .object({
      accentColor: z
        .string()
        .trim()
        .refine(isHexColor, 'Enter a hex color like #B71C1C.')
        .refine((v) => !isHexColor(v) || contrastWithWhite(v) >= MIN_CONTRAST, 'Pick a darker color: white text needs a contrast of 4.5:1 or more.'),
      paymentAccountIds: z.array(z.string()),
      deadline: z.enum(['24', '48', '72', 'custom']),
      customHours: z.string(),
    })
    .superRefine((v, ctx) => {
      if (hasAccounts && v.paymentAccountIds.length === 0) {
        ctx.addIssue({ code: 'custom', path: ['paymentAccountIds'], message: 'Pick at least one account for the billing email.' })
      }
      if (v.deadline === 'custom' && deadlineHours(v) === null) {
        ctx.addIssue({ code: 'custom', path: ['customHours'], message: 'Use 1 to 336 hours (14 days).' })
      }
    })
}

export function deadlineHours(v: Pick<EmailSettingsFormValues, 'deadline' | 'customHours'>): number | null {
  if (v.deadline !== 'custom') return Number(v.deadline)
  const raw = v.customHours.trim()
  const n = Number(raw)
  return /^\d{1,3}$/.test(raw) && n >= 1 && n <= 336 ? n : null
}

// ---- Proof upload -------------------------------------------------------------------------------

export const PROOF_MAX_BYTES = 5 * 1024 * 1024

export function checkProofFile(file: File): string | null {
  const okType = file.type === 'image/png' || file.type === 'image/jpeg' || (!file.type && /\.(png|jpe?g)$/i.test(file.name))
  if (!okType) return 'Upload a PNG or JPG image.'
  if (file.size > PROOF_MAX_BYTES) return 'The image is larger than 5 MB. Upload a smaller screenshot or photo.'
  return null
}

export const proofReferenceSchema = z.object({
  reference: z.string().trim().min(1, 'Enter the reference number from your receipt.').max(60, 'Use 60 characters or fewer.'),
})
export type ProofReferenceValues = z.infer<typeof proofReferenceSchema>

/** Keeps typed members and pads / trims to groupSize − 1 extra people. */
export function resizeMembers(current: MemberValues[], groupSize: number): MemberValues[] {
  const n = Math.max(0, groupSize - 1)
  return Array.from({ length: n }, (_, i) => current[i] ?? { ...emptyMember })
}

export function emptyRegistrationValues(ticketTypeId = '', groupSize = 1): RegistrationFormValues {
  return {
    ticketTypeId,
    buyer: { ...emptyBuyer },
    members: resizeMembers([], groupSize),
    voucherCode: '',
    consent: false,
    website: '',
    markPaid: false,
    payment: { method: 'GCash', paymentAccountId: '', reference: '', receivedAt: '' },
  }
}
