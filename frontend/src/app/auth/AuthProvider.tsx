import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { apiVoid, onTwoFactorRequired, onUnauthorized, resetCsrfToken } from '@/app/api'
import { AuthContext, type AuthApi } from './auth-context'
import { fetchMe, meQueryKey, type Me } from './me'

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const meQuery = useQuery({ queryKey: meQueryKey, queryFn: fetchMe, staleTime: 5 * 60_000, retry: false })
  const [twoFactorPending, setTwoFactorPending] = useState(false)

  useEffect(() => onTwoFactorRequired(() => setTwoFactorPending(true)), [])
  useEffect(
    () =>
      onUnauthorized(() => {
        queryClient.setQueryData(meQueryKey, null)
      }),
    [queryClient],
  )

  const me = meQuery.data
  const { refetch } = meQuery

  const signedIn = useCallback(
    (user: Me) => {
      resetCsrfToken() // api() already drops it after login; keep this for callers outside api()
      setTwoFactorPending(false)
      queryClient.setQueryData(meQueryKey, user)
    },
    [queryClient],
  )

  const twoFactorEnabled = useCallback(async () => {
    setTwoFactorPending(false)
    await queryClient.invalidateQueries({ queryKey: meQueryKey })
  }, [queryClient])

  const signOut = useCallback(async () => {
    try {
      await apiVoid('/api/auth/logout', { method: 'POST' })
    } finally {
      resetCsrfToken()
      setTwoFactorPending(false)
      queryClient.removeQueries({ predicate: (q) => q.queryKey[0] !== 'auth' })
      queryClient.setQueryData(meQueryKey, null)
    }
  }, [queryClient])

  const value = useMemo<AuthApi>(
    () => ({
      me,
      status: meQuery.isPending ? 'loading' : meQuery.isError ? 'error' : 'ready',
      error: meQuery.error,
      needsTwoFactorSetup: twoFactorPending || (!!me && me.twoFactorRequired),
      retry: () => void refetch(),
      signedIn,
      twoFactorEnabled,
      signOut,
    }),
    [me, meQuery.isPending, meQuery.isError, meQuery.error, twoFactorPending, refetch, signedIn, twoFactorEnabled, signOut],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
