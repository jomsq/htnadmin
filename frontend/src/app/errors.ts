import { isApiError } from './api'

export const RATE_LIMITED_MESSAGE = 'Too many attempts. Please wait a minute and try again.'
export const INVITE_INVALID_MESSAGE = 'This invite link is invalid or has expired. Ask an admin to resend it.'
export const RESET_INVALID_MESSAGE = 'This reset link is invalid or has expired.'

/** Human messages for the `code` extension the API puts on ProblemDetails. */
const codeMessages: Record<string, string> = {
  rate_limited: RATE_LIMITED_MESSAGE,
  invite_invalid: INVITE_INVALID_MESSAGE,
  reset_invalid: RESET_INVALID_MESSAGE,
  antiforgery: 'Your session security token expired. Refresh the page and try again.',
  already_active: 'This account is already active, so there is no invite to resend. Send a password reset link instead.',
  no_login: 'This person has no login. Members are roster only.',
  not_active: "This account isn't active yet. Resend the invite instead.",
  person_deactivated: 'This person is deactivated.',
  self_deactivate: "You can't deactivate your own account.",
  '2fa_already_enabled': 'Two-step verification is already on.',
  invalid_promotion: 'People move one step at a time: Member → Apprentice → Goon. Anyone can be seated on the Board.',
  email_locked: 'The email can only change before the person has activated their login.',
  duplicate_email: 'Someone with this email is already in the organization.',
  import_has_errors: 'Some rows have errors. Fix the file and preview it again.',
  self_change: "You can't change your own role. Ask another board member.",
  wrong_level: "This person's level doesn't fit that role.",
  low_contrast: 'This color is too light for white text. Pick a darker shade (contrast 4.5:1 or more).',
  no_tickets: 'Add at least one active ticket type before publishing.',
  duplicate_slug: 'Another event already uses this page link. Pick a different one.',
  slug_locked: 'The page link is locked once the event has been published.',
  price_locked: "The price can't change after tickets have been sold.",
  duplicate_code: 'This voucher code is already used for this event.',
  duplicate_main_event: 'Another event is already the main event for that membership year.',
  registration_closed: 'Registration for this event is closed.',
  group_size: 'Fill in the details for every member of the group.',
  not_pending: 'This registration is no longer waiting for payment.',
  not_paid: 'This registration is not paid yet.',
  too_many_uploads: 'Too many uploads for this registration. Reply to the billing email instead.',
  invalid_payment_account: "That payment account doesn't match the payment method.",
  unknown_payment_account: 'One of the payment accounts no longer exists.',
  no_email: 'Your account has no email address for the test.',
  rejected: "We couldn't submit the form. Please try again.",
  sold_out: 'This ticket just sold out. Pick another ticket.',
  ticket_unavailable: "This ticket isn't available anymore. Pick another ticket.",
  invalid_voucher: "This voucher code can't be used.",
  invalid_status: 'This registration changed in the meantime. Refresh and try again.',
  already_checked_in: "This attendee has already checked in, so the payment can't be reverted.",
  no_check_in: 'Only people who have checked in at an event can be Members.',
  proof_invalid: 'This upload link is invalid or has expired.',
}

/** Message for a known API error code, if any. */
export function messageForCode(code: string | undefined): string | undefined {
  return code ? codeMessages[code] : undefined
}

/**
 * Human message for a failed request. Known `code`s win; then `byStatus` overrides; then defaults.
 */
export function errorMessage(error: unknown, byStatus: Partial<Record<number, string>> = {}): string {
  if (isApiError(error)) {
    const byCode = messageForCode(error.code)
    if (byCode) return byCode
    if (error.status === 429) return RATE_LIMITED_MESSAGE
    const override = byStatus[error.status]
    if (override) return override
    if (error.status === 403) return 'You do not have access to do that.'
    if (error.status >= 500) return 'Something went wrong on our side. Try again in a moment.'
    return error.problem.detail ?? error.problem.title ?? 'The request could not be completed.'
  }
  return 'We could not reach HTN Admin. Check your connection and try again.'
}
