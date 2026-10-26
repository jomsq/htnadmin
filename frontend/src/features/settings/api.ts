import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { api, apiVoid } from '@/app/api'
import {
  brandingSchema,
  orgSettingsSchema,
  paymentAccountSchema,
  type BrandingFormValues,
  type OrgSettings,
  type PaymentAccountBody,
  type PointsSettings,
} from './schemas'

export const settingsKeys = {
  org: ['settings', 'org'] as const,
  paymentAccounts: ['settings', 'payment-accounts'] as const,
  paymentAccountList: (includeInactive: boolean) => ['settings', 'payment-accounts', { includeInactive }] as const,
}

export function useOrgSettingsQuery(enabled = true) {
  return useQuery({
    queryKey: settingsKeys.org,
    queryFn: () => api('/api/settings/org', orgSettingsSchema),
    enabled,
    staleTime: 5 * 60_000,
  })
}

export function useSavePointsMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    // The response body is not needed: the cache is updated from what was sent, then refetched.
    mutationFn: (body: PointsSettings) => apiVoid('/api/settings/points', { method: 'PUT', body }),
    onSuccess: (_data, body) => {
      queryClient.setQueryData<OrgSettings>(settingsKeys.org, (old) => (old ? { ...old, points: body } : old))
      return queryClient.invalidateQueries({ queryKey: settingsKeys.org })
    },
  })
}

export function useSaveBrandingMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: BrandingFormValues) => apiVoid('/api/settings/branding', { method: 'PUT', body }),
    onSuccess: (_data, body) => {
      queryClient.setQueryData<OrgSettings>(settingsKeys.org, (old) => (old ? { ...old, branding: { ...old.branding, ...body } } : old))
      return queryClient.invalidateQueries({ queryKey: settingsKeys.org })
    },
  })
}

export type LogoVariant = 'light' | 'dark'

export function useUploadLogoMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ variant, file }: { variant: LogoVariant; file: File }) => {
      const body = new FormData()
      body.append('file', file, file.name)
      return api(`/api/settings/branding/logo?variant=${variant}`, brandingSchema, { method: 'POST', body })
    },
    onSuccess: (branding) => {
      queryClient.setQueryData<OrgSettings>(settingsKeys.org, (old) => (old ? { ...old, branding } : old))
    },
  })
}

export function usePaymentAccountsQuery(includeInactive: boolean, enabled = true) {
  return useQuery({
    queryKey: settingsKeys.paymentAccountList(includeInactive),
    queryFn: () =>
      api(`/api/settings/payment-accounts${includeInactive ? '?includeInactive=true' : ''}`, z.array(paymentAccountSchema)),
    enabled,
  })
}

export function useSavePaymentAccountMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: PaymentAccountBody }) =>
      id
        ? api(`/api/settings/payment-accounts/${encodeURIComponent(id)}`, paymentAccountSchema, { method: 'PUT', body })
        : api('/api/settings/payment-accounts', paymentAccountSchema, { method: 'POST', body }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: settingsKeys.paymentAccounts }),
  })
}

export function useDeactivatePaymentAccountMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      apiVoid(`/api/settings/payment-accounts/${encodeURIComponent(id)}/deactivate`, { method: 'POST', body: { reason } }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: settingsKeys.paymentAccounts }),
  })
}
