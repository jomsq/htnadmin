import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiRequest, apiVoid } from '@/app/api'
import { eventKeys } from '@/features/events/api'
import {
  emailSettingsSchema,
  markPaidResultSchema,
  proofInfoSchema,
  publicEventSchema,
  registrationDetailSchema,
  registrationListSchema,
  registrationResultSchema,
  voucherCheckSchema,
  type EmailSettings,
  type EmailTemplate,
  type ManualRegistrationBody,
  type MarkPaidBody,
  type PublicRegistrationBody,
  type RegistrationStatus,
} from './schemas'

export type RegistrationFilter = { status?: RegistrationStatus; overdue?: boolean; search?: string; page: number; pageSize: number }

export const registrationKeys = {
  all: ['registrations'] as const,
  event: (eventId: string) => ['registrations', eventId] as const,
  lists: (eventId: string) => ['registrations', eventId, 'list'] as const,
  list: (eventId: string, f: RegistrationFilter) => ['registrations', eventId, 'list', f] as const,
  details: (eventId: string) => ['registrations', eventId, 'detail'] as const,
  detail: (eventId: string, id: string) => ['registrations', eventId, 'detail', id] as const,
  emailSettings: (eventId: string) => ['registrations', eventId, 'email-settings'] as const,
  publicEvent: (slug: string) => ['registrations', 'public', slug] as const,
  proof: (token: string) => ['registrations', 'proof', token] as const,
}

const enc = encodeURIComponent
const regPath = (eventId: string) => `/api/events/${enc(eventId)}/registrations`

/** Filters as query string (shared by the list and the CSV export link). */
export function filterQuery(f: Partial<RegistrationFilter>): string {
  const q = new URLSearchParams()
  if (f.status) q.set('status', f.status)
  if (f.overdue) q.set('overdue', 'true')
  if (f.search) q.set('search', f.search)
  if (f.page) q.set('page', String(f.page))
  if (f.pageSize) q.set('pageSize', String(f.pageSize))
  const s = q.toString()
  return s ? `?${s}` : ''
}

export function exportCsvUrl(eventId: string, f: Partial<RegistrationFilter>): string {
  return `${regPath(eventId)}/export.csv${filterQuery({ status: f.status, overdue: f.overdue, search: f.search })}`
}

// ---- Admin --------------------------------------------------------------------------------

export function useRegistrationsQuery(eventId: string, f: RegistrationFilter) {
  return useQuery({
    queryKey: registrationKeys.list(eventId, f),
    queryFn: () => api(`${regPath(eventId)}${filterQuery(f)}`, registrationListSchema),
    placeholderData: keepPreviousData,
  })
}

export function useRegistrationQuery(eventId: string, id: string | undefined) {
  return useQuery({
    queryKey: registrationKeys.detail(eventId, id ?? ''),
    queryFn: () => api(`${regPath(eventId)}/${enc(id ?? '')}`, registrationDetailSchema),
    enabled: !!id,
  })
}

/** After a write: refetch the list (counts change) and the open drawer; the event's sold count too. */
function useRegistrationsSettled(eventId: string) {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: registrationKeys.lists(eventId) }),
      queryClient.invalidateQueries({ queryKey: registrationKeys.details(eventId) }),
      queryClient.invalidateQueries({ queryKey: eventKeys.detail(eventId) }),
    ])
}

export function useMarkPaidMutation(eventId: string) {
  const settled = useRegistrationsSettled(eventId)
  return useMutation({
    mutationFn: (body: MarkPaidBody & { ids: string[] }) => api(`${regPath(eventId)}/mark-paid`, markPaidResultSchema, { method: 'POST', body }),
    onSuccess: () => settled(),
  })
}

export function useCancelRegistrationMutation(eventId: string) {
  const settled = useRegistrationsSettled(eventId)
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => apiVoid(`${regPath(eventId)}/${enc(id)}/cancel`, { method: 'POST', body: { reason } }),
    onSuccess: () => settled(),
  })
}

export function useRevertPaymentMutation(eventId: string) {
  const settled = useRegistrationsSettled(eventId)
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      apiVoid(`${regPath(eventId)}/${enc(id)}/revert-payment`, { method: 'POST', body: { reason } }),
    onSuccess: () => settled(),
  })
}

/** Resends one billing email per id (the API takes one registration at a time). Resolves with how many went out. */
export function useResendBillingMutation(eventId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (ids: string[]) => {
      const results = await Promise.allSettled(ids.map((id) => apiVoid(`${regPath(eventId)}/${enc(id)}/resend-billing`, { method: 'POST' })))
      const sent = results.filter((r) => r.status === 'fulfilled').length
      const failed = results.find((r): r is PromiseRejectedResult => r.status === 'rejected')
      if (sent === 0 && failed) throw failed.reason
      return { sent, failed: ids.length - sent }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: registrationKeys.details(eventId) }),
  })
}

export function useResendTicketMutation(eventId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiVoid(`${regPath(eventId)}/${enc(id)}/resend-ticket`, { method: 'POST' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: registrationKeys.details(eventId) }),
  })
}

export function useManualRegistrationMutation(eventId: string) {
  const settled = useRegistrationsSettled(eventId)
  return useMutation({
    mutationFn: async (body: ManualRegistrationBody) => {
      const { data } = await apiRequest(regPath(eventId), { method: 'POST', body })
      // The response shape may differ from the public one; only use it when it matches.
      const parsed = registrationResultSchema.partial().safeParse(data)
      return parsed.success ? parsed.data : {}
    },
    onSuccess: () => settled(),
  })
}

export function useEmailSettingsQuery(eventId: string) {
  return useQuery({
    queryKey: registrationKeys.emailSettings(eventId),
    queryFn: () => api(`/api/events/${enc(eventId)}/email-settings`, emailSettingsSchema),
  })
}

export function useSaveEmailSettingsMutation(eventId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: EmailSettings) => apiVoid(`/api/events/${enc(eventId)}/email-settings`, { method: 'PUT', body }),
    onSuccess: (_d, body) => {
      queryClient.setQueryData(registrationKeys.emailSettings(eventId), body)
      return queryClient.invalidateQueries({ queryKey: registrationKeys.emailSettings(eventId) })
    },
  })
}

export function useSendTestEmailMutation(eventId: string) {
  return useMutation({
    mutationFn: (template: EmailTemplate) => apiVoid(`/api/events/${enc(eventId)}/email-settings/test`, { method: 'POST', body: { template } }),
  })
}

// ---- Public (anonymous) -------------------------------------------------------------------

export function usePublicEventQuery(slug: string | undefined, options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: registrationKeys.publicEvent(slug ?? ''),
    queryFn: () => api(`/api/public/events/${enc(slug ?? '')}`, publicEventSchema),
    enabled: !!slug && (options.enabled ?? true),
    retry: false,
  })
}

export function useVoucherCheckMutation(slug: string) {
  return useMutation({
    mutationFn: (body: { code: string; ticketTypeId: string }) =>
      api(`/api/public/events/${enc(slug)}/voucher-check`, voucherCheckSchema, { method: 'POST', body }),
  })
}

export function useSubmitRegistrationMutation(slug: string) {
  return useMutation({
    mutationFn: (body: PublicRegistrationBody) =>
      api(`/api/public/events/${enc(slug)}/registrations`, registrationResultSchema, { method: 'POST', body }),
  })
}

export function useProofInfoQuery(token: string | undefined) {
  return useQuery({
    queryKey: registrationKeys.proof(token ?? ''),
    queryFn: () => api(`/api/public/proof/${enc(token ?? '')}`, proofInfoSchema),
    enabled: !!token,
    retry: false,
  })
}

export function useUploadProofMutation(token: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ file, reference }: { file: File; reference: string }) => {
      const body = new FormData()
      body.append('file', file, file.name)
      body.append('reference', reference)
      return apiVoid(`/api/public/proof/${enc(token)}`, { method: 'POST', body })
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: registrationKeys.proof(token) }),
  })
}
