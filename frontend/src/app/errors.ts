import { isApiError } from './api'

/** Human message for a failed request; `byStatus` overrides per HTTP status. */
export function errorMessage(error: unknown, byStatus: Partial<Record<number, string>> = {}): string {
  if (isApiError(error)) {
    const override = byStatus[error.status]
    if (override) return override
    if (error.status === 429) return 'Too many attempts. Wait a minute and try again.'
    if (error.status === 403) return 'You do not have access to do that.'
    if (error.status >= 500) return 'Something went wrong on our side. Try again in a moment.'
    return error.problem.detail ?? error.problem.title ?? 'The request could not be completed.'
  }
  return 'We could not reach HTN Admin. Check your connection and try again.'
}
