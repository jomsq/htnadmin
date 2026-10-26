import { createContext, useContext } from 'react'
import type { Me } from './me'
import type { Policy } from './policies'

export type AuthApi = {
  /** undefined while loading, null when signed out. */
  me: Me | null | undefined
  status: 'loading' | 'error' | 'ready'
  error: unknown
  /** Board account without 2FA, or the server answered 403 code=2fa_required. */
  needsTwoFactorSetup: boolean
  retry: () => void
  /** Store the user returned by login and refresh the CSRF token. */
  signedIn: (me: Me) => void
  /** Call after 2FA is enabled to reload /me. */
  twoFactorEnabled: () => Promise<void>
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthApi | null>(null)

export function useAuth(): AuthApi {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}

/** UI-only permission check. Hiding an action is never the real gate; the server is. */
export function useCan(policy: Policy): boolean {
  const { me } = useAuth()
  return me?.policies.includes(policy) ?? false
}
