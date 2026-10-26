import { createContext, useContext } from 'react'
import type { Preferences } from './preferences'

export type ThemeApi = {
  preferences: Preferences
  /** The theme actually applied (system resolved to light or dark). */
  resolvedTheme: 'light' | 'dark'
  setPreferences: (patch: Partial<Preferences>) => void
}

export const ThemeContext = createContext<ThemeApi | null>(null)

export function useTheme(): ThemeApi {
  const ctx = useContext(ThemeContext)
  if (!ctx) throw new Error('useTheme must be used inside <ThemeProvider>')
  return ctx
}
