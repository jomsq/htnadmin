import { useMutation, useQuery } from '@tanstack/react-query'
import { api, apiRequest, apiVoid, isApiError } from '@/app/api'
import { meSchema, type Me } from '@/app/auth/me'
import { inviteInfoSchema, loginAcceptedSchema, twoFactorSetupSchema, type InviteInfo } from './schemas'

export type LoginResult = { kind: 'signedIn'; me: Me } | { kind: 'twoFactor' }

export async function login(body: { email: string; password: string; rememberMe: boolean }): Promise<LoginResult> {
  const { status, data } = await apiRequest('/api/auth/login', { method: 'POST', body })
  if (status === 202 || loginAcceptedSchema.safeParse(data).success) return { kind: 'twoFactor' }
  return { kind: 'signedIn', me: meSchema.parse(data) }
}

export function useLoginMutation() {
  return useMutation({ mutationFn: login })
}

export function useLoginTwoFactorMutation() {
  return useMutation({
    // The server can't carry rememberMe across the two steps, so send it again with the code.
    mutationFn: (body: { code: string; rememberMe: boolean }) => api('/api/auth/login/2fa', meSchema, { method: 'POST', body }),
  })
}

export function useForgotPasswordMutation() {
  return useMutation({
    mutationFn: (body: { email: string }) => apiVoid('/api/auth/password/forgot', { method: 'POST', body }),
  })
}

export function useResetPasswordMutation() {
  return useMutation({
    /** `email` is optional: the token alone identifies the account. */
    mutationFn: (body: { token: string; email?: string; password: string }) =>
      apiVoid('/api/auth/password/reset', { method: 'POST', body }),
  })
}

/** Invite details for /invite/:token, or null when the link is invalid, used or expired (404). */
export async function fetchInvite(token: string): Promise<InviteInfo | null> {
  try {
    return await api(`/api/auth/invite/${encodeURIComponent(token)}`, inviteInfoSchema)
  } catch (e) {
    if (isApiError(e, 404)) return null
    throw e
  }
}

export function useInviteQuery(token: string) {
  return useQuery({
    queryKey: ['auth', 'invite', token],
    queryFn: () => fetchInvite(token),
    enabled: token.length > 0,
    retry: false,
    staleTime: Infinity,
  })
}

export function useAcceptInviteMutation() {
  return useMutation({
    mutationFn: (body: { token: string; password: string }) => apiVoid('/api/auth/invite/accept', { method: 'POST', body }),
  })
}

/** POST, but modelled as a query so StrictMode double-mounts reuse one key. */
export function useTwoFactorSetupQuery() {
  return useQuery({
    queryKey: ['auth', '2fa-setup'],
    queryFn: () => api('/api/auth/2fa/setup', twoFactorSetupSchema, { method: 'POST' }),
    staleTime: Infinity,
    gcTime: Infinity,
    retry: false,
  })
}

export function useEnableTwoFactorMutation() {
  return useMutation({
    mutationFn: (body: { code: string }) => apiVoid('/api/auth/2fa/enable', { method: 'POST', body }),
  })
}
