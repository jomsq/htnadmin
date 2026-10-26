import { z } from 'zod'
import { apiRequest, isApiError } from '@/app/api'
import { preferencesSchema } from '@/app/theme/preferences'

export const staffLevelSchema = z.enum(['Board', 'Goon', 'Apprentice'])
export type StaffLevel = z.infer<typeof staffLevelSchema>

export const meSchema = z.object({
  id: z.string(),
  /** Null for a login without a roster record (e.g. a bootstrap admin). */
  personId: z.string().nullable(),
  firstName: z.string(),
  lastName: z.string(),
  email: z.string(),
  level: staffLevelSchema.nullable(),
  isCfo: z.boolean(),
  twoFactorEnabled: z.boolean(),
  /** True only while the board 2FA gate applies (board account without 2FA). */
  twoFactorRequired: z.boolean(),
  policies: z.array(z.string()),
  preferences: preferencesSchema,
})

export type Me = z.infer<typeof meSchema>

export const meQueryKey = ['auth', 'me'] as const

/** Current user, or null when not signed in (401). */
export async function fetchMe(): Promise<Me | null> {
  try {
    const { data } = await apiRequest('/api/auth/me')
    return meSchema.parse(data)
  } catch (e) {
    if (isApiError(e, 401)) return null
    throw e
  }
}
