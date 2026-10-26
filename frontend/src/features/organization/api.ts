import { keepPreviousData, useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { api, apiVoid } from '@/app/api'
import {
  financeRolesSchema,
  importResultSchema,
  personDetailSchema,
  personSchema,
  peoplePageSchema,
  positionSchema,
  type CreatePersonBody,
  type EditPersonBody,
  type FinanceRolesBody,
  type Level,
  type PositionKind,
  type PromoteBody,
} from './schemas'

export type PeopleParams = { level?: Level; search?: string; page: number; pageSize: number }

export const peopleKeys = {
  all: ['people'] as const,
  list: (params: PeopleParams) => ['people', 'list', params] as const,
  counts: ['people', 'counts'] as const,
  detail: (id: string) => ['people', 'detail', id] as const,
}

export const positionKeys = {
  all: ['positions'] as const,
  financeRoles: ['finance-roles'] as const,
}

export function fetchPeople(params: PeopleParams) {
  const qs = new URLSearchParams()
  if (params.level) qs.set('level', params.level)
  if (params.search) qs.set('search', params.search)
  qs.set('page', String(params.page))
  qs.set('pageSize', String(params.pageSize))
  return api(`/api/people?${qs.toString()}`, peoplePageSchema)
}

export function usePeopleQuery(params: PeopleParams, enabled = true) {
  return useQuery({
    queryKey: peopleKeys.list(params),
    queryFn: () => fetchPeople(params),
    placeholderData: keepPreviousData,
    enabled,
  })
}

const countLevels: ReadonlyArray<Level | undefined> = [undefined, 'Board', 'Goon', 'Apprentice', 'Member']

/**
 * Counts for the level chips. The list endpoint has no counts, so this reads `total`
 * from a one-row page per level.
 */
export function useLevelCounts(): Partial<Record<Level | 'All', number>> {
  const results = useQueries({
    queries: countLevels.map((level) => ({
      queryKey: [...peopleKeys.counts, level ?? 'All'] as const,
      queryFn: () => fetchPeople({ level, page: 1, pageSize: 1 }),
      select: (d: z.infer<typeof peoplePageSchema>) => d.total,
    })),
  })
  const out: Partial<Record<Level | 'All', number>> = {}
  countLevels.forEach((level, i) => {
    const total = results[i].data
    if (total !== undefined) out[level ?? 'All'] = total
  })
  return out
}

export function usePersonQuery(id: string | undefined) {
  return useQuery({
    queryKey: peopleKeys.detail(id ?? ''),
    queryFn: () => api(`/api/people/${encodeURIComponent(id ?? '')}`, personDetailSchema),
    enabled: !!id,
  })
}

export function useCreatePersonMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreatePersonBody) => api('/api/people', personSchema, { method: 'POST', body }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: peopleKeys.all }),
  })
}

export function useUpdatePersonMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: EditPersonBody }) =>
      api(`/api/people/${encodeURIComponent(id)}`, personSchema, { method: 'PUT', body }),
    onSuccess: () => Promise.all([queryClient.invalidateQueries({ queryKey: peopleKeys.all }), queryClient.invalidateQueries({ queryKey: positionKeys.all })]),
  })
}

export function usePromotePersonMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: PromoteBody }) =>
      api(`/api/people/${encodeURIComponent(id)}/promote`, personSchema, { method: 'POST', body }),
    // Leaving Board/Goon vacates positions and clears finance roles, so refresh those too.
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: peopleKeys.all }),
        queryClient.invalidateQueries({ queryKey: positionKeys.all }),
        queryClient.invalidateQueries({ queryKey: positionKeys.financeRoles }),
      ]),
  })
}

export function useImportPeopleMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ file, dryRun }: { file: File; dryRun: boolean }) => {
      const body = new FormData()
      body.append('file', file, file.name)
      return api(`/api/people/import?dryRun=${dryRun}`, importResultSchema, { method: 'POST', body })
    },
    onSuccess: (_d, { dryRun }) => (dryRun ? undefined : queryClient.invalidateQueries({ queryKey: peopleKeys.all })),
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
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      apiVoid(`/api/people/${encodeURIComponent(id)}/deactivate`, { method: 'POST', body: { reason } }),
    onSuccess: () =>
      Promise.all([queryClient.invalidateQueries({ queryKey: peopleKeys.all }), queryClient.invalidateQueries({ queryKey: positionKeys.all })]),
  })
}

/** Emails an Active account a fresh reset link (expires in 30 minutes). 409 for no login / not active / deactivated. */
export function useSendPasswordResetMutation() {
  return useMutation({
    mutationFn: (id: string) => apiVoid(`/api/people/${encodeURIComponent(id)}/send-password-reset`, { method: 'POST' }),
  })
}

// ---- Positions ----------------------------------------------------------------------

export function usePositionsQuery() {
  return useQuery({ queryKey: positionKeys.all, queryFn: () => api('/api/positions', z.array(positionSchema)) })
}

function usePositionMutation<T>(fn: (vars: T) => Promise<unknown>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () =>
      Promise.all([queryClient.invalidateQueries({ queryKey: positionKeys.all }), queryClient.invalidateQueries({ queryKey: peopleKeys.all })]),
  })
}

export function useCreatePositionMutation() {
  return usePositionMutation((body: { kind: PositionKind; title: string }) =>
    api('/api/positions', positionSchema, { method: 'POST', body }),
  )
}

export function useUpdatePositionMutation() {
  return usePositionMutation(({ id, body }: { id: string; body: { title: string; isActive: boolean } }) =>
    apiVoid(`/api/positions/${encodeURIComponent(id)}`, { method: 'PUT', body }),
  )
}

export function useAssignPositionMutation() {
  return usePositionMutation(({ id, personId }: { id: string; personId: string }) =>
    apiVoid(`/api/positions/${encodeURIComponent(id)}/assign`, { method: 'POST', body: { personId } }),
  )
}

export function useVacatePositionMutation() {
  return usePositionMutation(({ id, reason }: { id: string; reason: string | null }) =>
    apiVoid(`/api/positions/${encodeURIComponent(id)}/vacate`, { method: 'POST', body: reason ? { reason } : {} }),
  )
}

// ---- Finance roles (board only) -------------------------------------------------------

export function useFinanceRolesQuery(enabled: boolean) {
  return useQuery({ queryKey: positionKeys.financeRoles, queryFn: () => api('/api/finance-roles', financeRolesSchema), enabled })
}

export function useUpdateFinanceRolesMutation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: FinanceRolesBody) => apiVoid('/api/finance-roles', { method: 'PUT', body }),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: positionKeys.financeRoles }),
        queryClient.invalidateQueries({ queryKey: peopleKeys.all }),
      ]),
  })
}
