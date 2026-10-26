import { useCallback, useLayoutEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiVoid } from '@/app/api'
import { useAuth } from '@/app/auth/auth-context'
import { meQueryKey, type Me } from '@/app/auth/me'
import { useToast } from '@/components/ui/toast-context'
import { ThemeContext } from './theme-context'
import { loadLocalPreferences, saveLocalPreferences, type Preferences } from './preferences'

const DARK_QUERY = '(prefers-color-scheme: dark)'

function subscribeSystemTheme(cb: () => void) {
  const mql = window.matchMedia?.(DARK_QUERY)
  if (!mql) return () => {}
  mql.addEventListener('change', cb)
  return () => mql.removeEventListener('change', cb)
}

function systemPrefersDark() {
  return window.matchMedia?.(DARK_QUERY).matches ?? false
}

/**
 * Applies display preferences to <html>: data-theme (light|dark), data-text-size, data-density.
 * Signed in: preferences come from /me and are saved with PUT /api/me/preferences.
 * Signed out: localStorage.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const { me } = useAuth()
  const queryClient = useQueryClient()
  const { toast } = useToast()
  const [localPrefs, setLocalPrefs] = useState<Preferences>(loadLocalPreferences)
  const preferences = me?.preferences ?? localPrefs
  const systemDark = useSyncExternalStore(subscribeSystemTheme, systemPrefersDark, () => false)
  const resolvedTheme = preferences.mode === 'system' ? (systemDark ? 'dark' : 'light') : preferences.mode

  useLayoutEffect(() => {
    const root = document.documentElement
    root.dataset.theme = resolvedTheme
    root.dataset.mode = preferences.mode
    root.dataset.textSize = preferences.textSize
    root.dataset.density = preferences.density
  }, [resolvedTheme, preferences.mode, preferences.textSize, preferences.density])

  const save = useMutation({
    mutationFn: (p: Preferences) => apiVoid('/api/me/preferences', { method: 'PUT', body: p }),
    onError: () => {
      toast({ tone: 'error', title: 'Could not save your display settings.', description: 'They apply on this device for now.' })
      void queryClient.invalidateQueries({ queryKey: meQueryKey })
    },
  })
  const { mutate } = save

  const setPreferences = useCallback(
    (patch: Partial<Preferences>) => {
      const next = { ...preferences, ...patch }
      setLocalPrefs(next)
      saveLocalPreferences(next)
      if (me) {
        queryClient.setQueryData<Me | null>(meQueryKey, (old) => (old ? { ...old, preferences: next } : old))
        mutate(next)
      }
    },
    [preferences, me, queryClient, mutate],
  )

  const value = useMemo(() => ({ preferences, resolvedTheme, setPreferences }), [preferences, resolvedTheme, setPreferences])
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}
