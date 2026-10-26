import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiVoid } from '@/app/api'
import { personSchema, peoplePageSchema, type CreatePersonBody, type Level } from './schemas'

export type PeopleParams = { level?: Level; search?: string; page: number; pageSize: number }

export const peopleKeys = {
  all: ['people'] as const,
  list: (params: PeopleParams) => ['people', 'list', params] as const,
}

export function fetchPeople(params: PeopleParams) {
  const qs = new URLSearchParams()
  if (params.level) qs.set('level', params.level)
  if (params.search) qs.set('search', params.search)
  qs.set('page', String(params.page))
  qs.set('pageSize', String(params.pageSize))
  return api(`/api/people?${qs.toString()}`, peoplePageSchema)
}

export function usePeopleQuery(params: PeopleParams) {
  return useQuery({
    queryKey: peopleKeys.list(params),
    queryFn: () => fetchPeople(params),
    placeholderData: keepPreviousData,
  })
}

export function useCreatePersonMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreatePersonBody) => api('/api/people', personSchema, { method: 'POST', body }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: peopleKeys.all }),
  })
}

export function useResendInviteMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiVoid(`/api/people/${encodeURIComponent(id)}/resend-invite`, { method: 'POST' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: peopleKeys.all }),
  })
}

export function useDeactivatePersonMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => apiVoid(`/api/people/${encodeURIComponent(id)}/deactivate`, { method: 'POST' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: peopleKeys.all }),
  })
}

/** Emails an Active account a fresh reset link (expires in 30 minutes). 409 for no login / not active / deactivated. */
export function useSendPasswordResetMutation() {
  return useMutation({
    mutationFn: (id: string) => apiVoid(`/api/people/${encodeURIComponent(id)}/send-password-reset`, { method: 'POST' }),
  })
}
