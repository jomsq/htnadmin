import type { FieldPath, UseFormSetError } from 'react-hook-form'
import { isApiError } from '@/app/api'
import { formPathForServerField, type RegistrationFormValues } from '../schemas'

/** Puts 400 field errors from the API on the matching form fields. Returns the form paths that got an error. */
export function applyRegistrationFieldErrors(e: unknown, setError: UseFormSetError<RegistrationFormValues>): string[] {
  if (!isApiError(e, 400)) return []
  const applied: string[] = []
  for (const [key, message] of Object.entries(e.fieldErrors)) {
    const path = formPathForServerField(key)
    if (!path) continue
    // formPathForServerField only returns paths that exist on RegistrationFormValues.
    setError(path as FieldPath<RegistrationFormValues>, { type: 'server', message })
    applied.push(path)
  }
  return applied
}
