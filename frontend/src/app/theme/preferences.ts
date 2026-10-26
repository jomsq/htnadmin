import { z } from 'zod'

export const modeSchema = z.enum(['light', 'system', 'dark'])
export const textSizeSchema = z.enum(['default', 'large'])
export const densitySchema = z.enum(['comfortable', 'compact'])

export const preferencesSchema = z.object({
  mode: modeSchema,
  textSize: textSizeSchema,
  density: densitySchema,
})

export type Mode = z.infer<typeof modeSchema>
export type TextSize = z.infer<typeof textSizeSchema>
export type Density = z.infer<typeof densitySchema>
export type Preferences = z.infer<typeof preferencesSchema>

export const defaultPreferences: Preferences = { mode: 'system', textSize: 'default', density: 'comfortable' }

/** localStorage key; also read by the inline script in index.html to avoid a theme flash. */
export const PREFERENCES_STORAGE_KEY = 'htn.preferences'

export function loadLocalPreferences(): Preferences {
  try {
    const raw = window.localStorage.getItem(PREFERENCES_STORAGE_KEY)
    if (!raw) return defaultPreferences
    const parsed = preferencesSchema.safeParse(JSON.parse(raw))
    return parsed.success ? parsed.data : defaultPreferences
  } catch {
    return defaultPreferences
  }
}

export function saveLocalPreferences(p: Preferences): void {
  try {
    window.localStorage.setItem(PREFERENCES_STORAGE_KEY, JSON.stringify(p))
  } catch {
    // storage unavailable (private mode); preferences still apply for this session
  }
}
