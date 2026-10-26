import { z } from 'zod'
import { apiRequest, isApiError } from '@/app/api'
import { preferencesSchema } from '@/app/theme/preferences'

export const staffLevelSchema = z.enum(['Board', 'Goon', 'Apprentice'])
export type StaffLevel = z.infer<typeof staffLevelSchema>

export const meSchema = z.object({
  id: z.string(),
  personId: z.string(),
  firstName: z.string(),
  lastName: z.string(),
  email: z.string(),
  level: staffLevelSchema,
  isCfo: z.boolean(),
  twoFactorEnabled: z.boolean(),
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
